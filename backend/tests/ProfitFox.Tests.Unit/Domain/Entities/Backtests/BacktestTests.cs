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
}