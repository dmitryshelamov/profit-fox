using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Backtests;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Entities.Backtests;

/// <summary>Requirement "Статусы бэктеста": Pending → LoadingCandles → Running → Succeeded, Failed or Cancelled.</summary>
public class BacktestTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:30:00Z");

    [Test]
    public void Pending_backtest_starts_loading_candles()
    {
        Backtest loading = BacktestGenerator.Generate(BacktestStatus.Pending).StartLoading(Now);

        (loading.Status, loading.StartedAt).ShouldBe((BacktestStatus.LoadingCandles, Now));
    }

    [Test]
    public void Loading_backtest_waits_for_an_import_job_and_shows_the_loaded_candles()
    {
        var job = Guid.NewGuid();

        Backtest loading = BacktestGenerator.Generate(BacktestStatus.LoadingCandles).WaitForImport(job).Loaded(5, 12);

        (loading.ImportJobId, loading.LoadedCandles, loading.ExpectedCandles).ShouldBe((job, 5L, 12L));
    }

    [Test]
    public void Loaded_backtest_runs_from_the_first_stored_candle_and_knows_its_gaps()
    {
        DateTimeOffset first = Now.AddDays(-1);
        var gap = new CandleGap(Now.AddDays(-2), first);

        Backtest running = BacktestGenerator.Generate(BacktestStatus.LoadingCandles).StartRunning(first, [gap]);

        (running.Status, running.ActualStart, running.Gaps.ShouldHaveSingleItem()).ShouldBe((BacktestStatus.Running, first, gap));
    }

    [Test]
    public void Progress_counts_calculated_combinations_and_a_new_result_ends_the_stalled_restarts()
    {
        Backtest running = BacktestGenerator.Generate(BacktestStatus.Running) with { StalledRestarts = 2, CalculatedCombinations = 4 };

        Backtest progressed = running.Progress(5, 1234.5m);

        (progressed.CalculatedCombinations, progressed.Speed, progressed.StalledRestarts).ShouldBe((5L, 1234.5m, 0));
    }

    [Test]
    public void Running_backtest_succeeds()
    {
        Backtest succeeded = BacktestGenerator.Generate(BacktestStatus.Running).Succeed(Now);

        (succeeded.Status, succeeded.FinishedAt).ShouldBe((BacktestStatus.Succeeded, Now));
    }

    [Test]
    public void Backtest_fails_with_a_reason()
    {
        Backtest failed = BacktestGenerator.Generate(BacktestStatus.LoadingCandles).Fail("Bybit is unavailable.", Now);

        (failed.Status, failed.Error, failed.FinishedAt).ShouldBe((BacktestStatus.Failed, "Bybit is unavailable.", Now));
    }

    // Scenario "Бэктест без продвижения": the third restart in a row without a new result fails the backtest.
    [TestCase(0, BacktestStatus.Running, 1)]
    [TestCase(1, BacktestStatus.Running, 2)]
    [TestCase(2, BacktestStatus.Failed, 3)]
    public void Restart_of_a_running_backtest_counts_a_stalled_restart(int stalled, BacktestStatus status, int counted)
    {
        Backtest restarted = (BacktestGenerator.Generate(BacktestStatus.Running) with { StalledRestarts = stalled }).Restarted(Now);

        (restarted.Status, restarted.StalledRestarts).ShouldBe((status, counted));
        restarted.Error.ShouldBe(status == BacktestStatus.Failed ? Backtest.InterruptedByRestart : null);
    }
}