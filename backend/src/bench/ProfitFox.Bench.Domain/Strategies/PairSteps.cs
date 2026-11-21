using ProfitFox.Bench.Domain.MarketData;
using ProfitFox.Bench.Domain.Signals;
using ProfitFox.Engine.Strategy.Execution;
using ProfitFox.Engine.Strategy.Graph;

namespace ProfitFox.Bench.Domain.Strategies;

/// <summary>
///     The steps of a strategy on one pair, as a run of the bench sees them (spec hypothesis-bench «Гипотеза исполняется
///     как стратегия»): the executor gets the candles a signal gets (<see cref="PairCandles" />), so the orders of the
///     strategy of a hypothesis come at the events of its signal. Every order is kept, in time order; a run takes those
///     whose trade fits its period.
/// </summary>
public sealed class PairSteps
{
    private readonly StrategyStep[] _steps;
    private readonly DateTimeOffset[] _moments;
    private readonly StrategyStep _beforeCandles;

    /// <summary>
    ///     The steps with an entry order, in time order.
    /// </summary>
    public IReadOnlyList<StrategyStep> Orders { get; }

    /// <summary>
    ///     The steps of a pair in time order, and what the strategy gives before its first candle.
    /// </summary>
    public PairSteps(IReadOnlyList<StrategyStep> steps, StrategyStep beforeCandles)
    {
        _steps = [.. steps];
        _moments = [.. steps.Select(step => step.Moment)];
        _beforeCandles = beforeCandles;
        Orders = [.. steps.Where(step => step.Entry is not null)];
    }

    public static PairSteps Of(
        TradingStrategy strategy,
        TimeSpan timeframe,
        string symbol,
        IEnumerable<HistoryCandle> candles,
        IEnumerable<HistoryPayment> funding)
    {
        var executor = new StrategyExecutor(strategy);
        List<StrategyStep> steps = [];
        foreach ((ClosedCandle closed, FundingPayment? lastFunding) in PairCandles.Closed(timeframe, candles, funding))
        {
            steps.Add(executor.Step(symbol, closed, lastFunding));
        }

        return new PairSteps(steps, executor.BeforeCandles(DateTimeOffset.UnixEpoch));
    }

    /// <summary>
    ///     The step a cycle is opened outside the graph with at <paramref name="moment" /> (spec strategy-position «Открытие
    ///     вне графа»): no order, and the values the exit nodes have at this moment. A random entry falls on a close of the
    ///     grid of the timeframe, where the pair may have no computed candle — a candle without trades, a hole in the
    ///     history, a moment before the first candle. The values are then those of the last candle closed by the moment:
    ///     the graph was not computed since. Before the first candle only the values written in the graph are known.
    /// </summary>
    public StrategyStep At(DateTimeOffset moment)
    {
        int index = Array.BinarySearch(_moments, moment);
        if (index < 0)
        {
            index = ~index - 1;
        }

        StrategyStep step = index >= 0 ? _steps[index] : _beforeCandles;
        return step.Moment == moment && step.Entry is null ? step : step with { Moment = moment, Entry = null };
    }
}
