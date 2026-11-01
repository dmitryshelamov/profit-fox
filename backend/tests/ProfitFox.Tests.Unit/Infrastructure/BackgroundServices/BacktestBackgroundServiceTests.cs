using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using ProfitFox.Application.Backtests.Commands.RunBacktest;
using ProfitFox.Infrastructure.BackgroundServices;
using ProfitFox.Infrastructure.Messaging;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.BackgroundServices;

public class BacktestBackgroundServiceTests
{
    // Scenario "Очередь".
    [Test]
    public async Task Backtest_queued_while_another_runs_starts_only_after_it()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var firstStarted = new TaskCompletionSource();
        var firstFinish = new TaskCompletionSource();
        var secondStarted = new TaskCompletionSource();
        ISender sender = Substitute.For<ISender>();
        sender.Send(new RunBacktestCommand(first), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            firstStarted.SetResult();
            await firstFinish.Task;
        });
        sender.Send(new RunBacktestCommand(second), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            secondStarted.SetResult();

            return Task.CompletedTask;
        });
        await using ServiceProvider services = new ServiceCollection().AddSingleton(sender).BuildServiceProvider();
        var queue = new BacktestQueue();
        using var service = new BacktestBackgroundService(queue, services.GetRequiredService<IServiceScopeFactory>(), NullLogger<BacktestBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await queue.EnqueueAsync(first, CancellationToken.None);
        await queue.EnqueueAsync(second, CancellationToken.None);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Task.Delay(100);
        secondStarted.Task.IsCompleted.ShouldBeFalse();
        firstFinish.SetResult();
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task Failed_backtest_does_not_stop_the_next_one()
    {
        var failing = Guid.NewGuid();
        var next = Guid.NewGuid();
        var nextRan = new TaskCompletionSource();
        ISender sender = Substitute.For<ISender>();
        sender.Send(new RunBacktestCommand(failing), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("database is down"));
        sender.Send(new RunBacktestCommand(next), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            nextRan.SetResult();

            return Task.CompletedTask;
        });
        await using ServiceProvider services = new ServiceCollection().AddSingleton(sender).BuildServiceProvider();
        var queue = new BacktestQueue();
        using var service = new BacktestBackgroundService(queue, services.GetRequiredService<IServiceScopeFactory>(), NullLogger<BacktestBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await queue.EnqueueAsync(failing, CancellationToken.None);
        await queue.EnqueueAsync(next, CancellationToken.None);

        await nextRan.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.ExecuteTask!.IsFaulted.ShouldBeFalse();
        await service.StopAsync(CancellationToken.None);
    }
}