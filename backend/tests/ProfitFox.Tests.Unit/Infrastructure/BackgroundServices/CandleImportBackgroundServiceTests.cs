using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        sender.Send(new RunCandleImportCommand(next), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                nextRan.SetResult();

                return Task.CompletedTask;
            });
        await using ServiceProvider services = new ServiceCollection().AddSingleton(sender).BuildServiceProvider();
        var queue = new CandleImportQueue();
        using var service = new CandleImportBackgroundService(
            queue,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<CandleImportBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await queue.EnqueueAsync(failing, CancellationToken.None);
        await queue.EnqueueAsync(next, CancellationToken.None);

        await nextRan.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.ExecuteTask!.IsFaulted.ShouldBeFalse();
        await service.StopAsync(CancellationToken.None);
    }

    // The job stays Running and resumes after the next start, so a shutdown is no failure of the job.
    [Test]
    public async Task Job_cancelled_by_shutdown_is_not_logged_as_a_failure()
    {
        var job = Guid.NewGuid();
        ISender sender = Substitute.For<ISender>();
        var jobStarted = new TaskCompletionSource();
        sender.Send(new RunCandleImportCommand(job), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                jobStarted.SetResult();
                await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
            });
        await using ServiceProvider services = new ServiceCollection().AddSingleton(sender).BuildServiceProvider();
        // NSubstitute cannot proxy ILogger<T> of an internal type, so the logger writes to a substitute of the plain ILogger.
        ILogger log = Substitute.For<ILogger>();
        ILoggerFactory loggers = Substitute.For<ILoggerFactory>();
        loggers.CreateLogger(Arg.Any<string>()).Returns(log);
        var queue = new CandleImportQueue();
        using var service = new CandleImportBackgroundService(
            queue,
            services.GetRequiredService<IServiceScopeFactory>(),
            new Logger<CandleImportBackgroundService>(loggers));

        await service.StartAsync(CancellationToken.None);
        await queue.EnqueueAsync(job, CancellationToken.None);
        await jobStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        log.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log)).ShouldBeEmpty();
    }
}