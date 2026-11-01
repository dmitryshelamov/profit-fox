using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Backtests.Commands.RequeueInterruptedBacktests;
using ProfitFox.Application.Ports.Backtests;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Backtests;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application;

// Choosing unfinished backtests and their order is the repository's part, see its integration tests.
public class RequeueInterruptedBacktestsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:30:00Z");

    private IBacktestRepository _backtests = null!;
    private IBacktestQueue _queue = null!;

    [SetUp]
    public void SetUp()
    {
        _backtests = Substitute.For<IBacktestRepository>();
        _queue = Substitute.For<IBacktestQueue>();
    }

    [Test]
    public async Task Unfinished_backtests_are_queued_again_in_their_order_and_a_running_one_counts_a_stalled_restart()
    {
        Backtest pending = BacktestGenerator.Generate(BacktestStatus.Pending);
        Backtest loading = BacktestGenerator.Generate(BacktestStatus.LoadingCandles);
        Backtest running = BacktestGenerator.Generate(BacktestStatus.Running) with { StalledRestarts = 1 };
        _backtests.GetUnfinishedAsync(Arg.Any<CancellationToken>()).Returns([running, pending, loading]);

        await Handler().Handle(new RequeueInterruptedBacktestsCommand(), CancellationToken.None);

        await _backtests.Received(1).UpdateAsync(running with { StalledRestarts = 2 }, BacktestStatus.Running, Arg.Any<CancellationToken>());
        await _backtests.Received(1).UpdateAsync(Arg.Any<Backtest>(), Arg.Any<BacktestStatus>(), Arg.Any<CancellationToken>());
        Queued().ShouldBe([running.Id, pending.Id, loading.Id]);
    }

    // Scenario "Бэктест без продвижения".
    [Test]
    public async Task Third_restart_without_a_new_result_fails_the_backtest_and_does_not_queue_it()
    {
        Backtest running = BacktestGenerator.Generate(BacktestStatus.Running) with { StalledRestarts = 2 };
        _backtests.GetUnfinishedAsync(Arg.Any<CancellationToken>()).Returns([running]);

        await Handler().Handle(new RequeueInterruptedBacktestsCommand(), CancellationToken.None);

        await _backtests.Received(1).UpdateAsync(
            running with { StalledRestarts = 3, Status = BacktestStatus.Failed, Error = Backtest.InterruptedByRestart, FinishedAt = Now },
            BacktestStatus.Running,
            Arg.Any<CancellationToken>());
        Queued().ShouldBeEmpty();
    }

    private RequeueInterruptedBacktestsCommandHandler Handler()
    {
        return new RequeueInterruptedBacktestsCommandHandler(_backtests, _queue, new FakeTimeProvider(Now));
    }

    private IReadOnlyList<Guid> Queued()
    {
        return [.. _queue.ReceivedCalls().Select(call => (Guid)call.GetArguments()[0]!)];
    }
}