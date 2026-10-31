using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using ProfitFox.Application.MarketData.Commands.RunCandleImport;
using ProfitFox.Infrastructure.BackgroundServices;
using ProfitFox.Infrastructure.Messaging;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.BackgroundServices;

public class CandleImportBackgroundServiceTests
{
    [Test]
    public async Task Failed_job_does_not_stop_the_next_one()
    {
        var failing = Guid.NewGuid();
        var next = Guid.NewGuid();
        ISender sender = Substitute.For<ISender>();
        sender.Send(new RunCandleImportCommand(failing), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("database is down"));
        var nextRan = new TaskCompletionSource();
        sender.Send(new RunCandleImportCommand(next), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            nextRan.SetResult();
            return Task.CompletedTask;
        });
        await using ServiceProvider services = new ServiceCollection().AddSingleton(sender).BuildServiceProvider();
        var queue = new CandleImportQueue();
        using var service = new CandleImportBackgroundService(
            queue, services.GetRequiredService<IServiceScopeFactory>(), NullLogger<CandleImportBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await queue.EnqueueAsync(failing, CancellationToken.None);
        await queue.EnqueueAsync(next, CancellationToken.None);

        await nextRan.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.ExecuteTask!.IsFaulted.ShouldBeFalse();
        await service.StopAsync(CancellationToken.None);
    }
}
