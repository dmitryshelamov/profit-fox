using MediatR;

using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.Backtests.Commands.RunBacktest;

public sealed class RunBacktestCommandHandler(
    IBacktestRepository backtests,
    IStrategyLibraryRepository library,
    ICandleRepository candles,
    TimeProvider time) : IRequestHandler<RunBacktestCommand>
{
    /// <summary>Takes the backtest through its statuses from where it is: loading candles, then the combinations without a result.</summary>
    public async Task Handle(RunBacktestCommand request, CancellationToken cancellationToken)
    {
        Backtest backtest = (await backtests.GetAsync(request.BacktestId, cancellationToken))!;

        if (backtest.Status == BacktestStatus.Pending)
        {
            Backtest loading = backtest.StartLoading(time.GetUtcNow());
            await backtests.UpdateAsync(loading, BacktestStatus.Pending, cancellationToken);
            backtest = loading;
        }

        if (backtest.Status == BacktestStatus.LoadingCandles)
        {
            IReadOnlyList<DateTimeOffset> stored = await candles.GetOpenTimesAsync(backtest.Instrument, backtest.Interval, backtest.From, backtest.To, cancellationToken);
            IReadOnlyList<CandleGap> gaps = [.. backtest.Interval.MissingRanges(stored, backtest.From, backtest.To).Select(gap => new CandleGap(gap.From, gap.To))];
            Backtest running = backtest.StartRunning(stored.Count > 0 ? stored[0] : null, gaps);
            await backtests.UpdateAsync(running, BacktestStatus.LoadingCandles, cancellationToken);
            backtest = running;
        }

        await RunAsync(backtest, cancellationToken);
    }

    private async Task RunAsync(Backtest backtest, CancellationToken cancellationToken)
    {
        StrategyVersion version = (await library.GetVersionAsync(backtest.StrategyId, backtest.StrategyVersion, cancellationToken))!;
        IReadOnlyList<Candle> period = await candles.GetAsync(backtest.Instrument, backtest.Interval, backtest.From, backtest.To, int.MaxValue, cancellationToken);
        ParameterGrid grid = backtest.Grid();
        var results = new List<BacktestResult>();

        for (long combination = 0; combination < backtest.TotalCombinations; combination++)
        {
            long started = time.GetTimestamp();
            IReadOnlyDictionary<string, NodeValue> values = grid.Combination(combination);
            var metrics = new MetricsObserver(backtest.Simulation.Balance);
            backtest.Replay(version.Snapshot, values).Run(backtest.From, period, metrics);
            results.Add(new BacktestResult(backtest.Id, combination, values, metrics.Metrics(), time.GetElapsedTime(started)));
        }

        Backtest calculated = backtest.Progress(results.Count, null);
        await backtests.SaveProgressAsync(calculated, results, cancellationToken);
        await backtests.UpdateAsync(calculated.Succeed(time.GetUtcNow()), BacktestStatus.Running, cancellationToken);
    }
}