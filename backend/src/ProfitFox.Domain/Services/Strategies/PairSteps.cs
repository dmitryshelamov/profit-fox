using ProfitFox.Domain.Services.Execution;
using ProfitFox.Domain.Services.Signals;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Services.Strategies;

/// <summary>
///     The steps of a strategy on one pair, as a backtest sees them (spec strategy-backtest «Сделки набора»): the executor
///     gets the candles a signal gets (<see cref="PairCandles" />), so the orders of a strategy come at the events of its
///     signal. Every order is kept, in time order; a backtest takes those whose trade fits its history.
/// </summary>
public sealed class PairSteps
{
    private readonly StrategyStep[] _steps;
    private readonly DateTimeOffset[] _moments;
    private readonly StrategyStep _beforeCandles;
    private readonly bool _byPlan;

    /// <summary>
    ///     The steps with an entry order, in time order.
    /// </summary>
    public IReadOnlyList<StrategyStep> Orders { get; }

    /// <summary>
    ///     The steps of a pair in time order, and what the strategy gives before its first candle.
    /// </summary>
    public PairSteps(IReadOnlyList<StrategyStep> steps, StrategyStep beforeCandles, bool byPlan = true)
    {
        _byPlan = byPlan;
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
        IEnumerable<HistoryPayment> funding,
        IReadOnlyDictionary<string, IReadOnlyList<HistoryCandle>>? named = null,
        IEnumerable<OpenInterestPoint>? openInterest = null)
    {
        var executor = new StrategyExecutor(strategy);
        List<StrategyStep> steps = [];
        foreach ((ClosedCandle closed, CandleFacts facts, IReadOnlyDictionary<string, ClosedCandle>? pairs) in PairCandles.WithPairs(timeframe, candles, funding, named, openInterest))
        {
            steps.Add(executor.Step(symbol, closed, facts, pairs));
        }

        return new PairSteps(steps, executor.BeforeCandles(DateTimeOffset.UnixEpoch), strategy.ByPlan);
    }

    /// <summary>
    ///     The longest time the steps in [<paramref name="from" />, <paramref name="to" />) give, with the step that holds at
    ///     <paramref name="from" />: what a cycle opened in that part of time, by an order or outside the graph, may last.
    ///     Zero when no step gives a time.
    /// </summary>
    public TimeSpan Longest(DateTimeOffset from, DateTimeOffset to)
    {
        int first = Array.BinarySearch(_moments, from);
        first = first < 0 ? ~first : first;
        TimeSpan longest = (first > 0 ? _steps[first - 1] : _beforeCandles).After ?? TimeSpan.Zero;
        for (int index = first; index < _steps.Length && _moments[index] < to; index++)
        {
            if (_steps[index].After is { } after && after > longest)
            {
                longest = after;
            }
        }

        return longest;
    }

    /// <summary>
    ///     The step a cycle is opened outside the graph with at <paramref name="moment" /> (spec strategy-position «Открытие
    ///     вне графа»): no order, and the values the exit nodes have at this moment. A random entry falls on a close of the
    ///     grid of the timeframe, where the pair may have no computed candle — a candle without trades, a hole in the
    ///     history, a moment before the first candle. The values are then those of the last candle closed by the moment:
    ///     the graph was not computed since. Before the first candle only the values written in the graph are known.
    /// </summary>
    /// <exception cref="InvalidOperationException">The strategy has orders: only a cycle of a strategy by plan opens outside the graph.</exception>
    public StrategyStep At(DateTimeOffset moment)
    {
        if (!_byPlan)
        {
            throw new InvalidOperationException("Only a cycle of a strategy by plan opens outside the graph; this strategy has orders.");
        }

        int index = Array.BinarySearch(_moments, moment);
        if (index < 0)
        {
            index = ~index - 1;
        }

        StrategyStep step = index >= 0 ? _steps[index] : _beforeCandles;
        return step.Moment == moment && step.Entry is null ? step : step with { Moment = moment, Entry = null, Node = null };
    }
}
