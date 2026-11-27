using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph;

namespace ProfitFox.Domain.Services.Execution;

/// <summary>
///     A strategy running on many pairs (spec strategy-execution): one run per pair, so the state of one pair never
///     reaches another and the order in which pairs come does not matter. At each closed candle it gives the entry order,
///     if the event of an <c>enter</c> node happened, and the values of the exit nodes of this candle.
/// </summary>
public sealed class StrategyExecutor
{
    private readonly TradingStrategy _strategy;
    private readonly CompiledGraph _graph;
    private readonly Dictionary<string, StrategyRun> _runs = new Dictionary<string, StrategyRun>(StringComparer.Ordinal);

    public StrategyExecutor(TradingStrategy strategy)
    {
        _strategy = strategy;
        _graph = CompiledGraph.Compile(strategy.Graph);
    }

    /// <summary>
    ///     The step of the pair at the close of <paramref name="candle" />. Candles of one pair go in the order of their
    ///     close; pairs may be mixed in any order. <paramref name="pairs" /> are the candles of the pairs the graph names.
    /// </summary>
    public StrategyStep Step(string instrument, ClosedCandle candle, CandleFacts facts, IReadOnlyDictionary<string, ClosedCandle>? pairs = null)
    {
        if (!_runs.TryGetValue(instrument, out StrategyRun? run))
        {
            run = new StrategyRun(_strategy, _graph);
            _runs.Add(instrument, run);
        }

        return run.Step(candle, facts, pairs);
    }

    /// <summary>
    ///     Forgets the run of the pair: its next candle starts a run from nothing (task 18.8), as a pair warmed up again
    ///     needs.
    /// </summary>
    public void Forget(string instrument)
    {
        _runs.Remove(instrument);
    }

    /// <summary>
    ///     What the strategy gives at a moment before any candle of a pair: no order, and of the exit values only those
    ///     written in the graph; a value that comes by a wire is still missing.
    /// </summary>
    public StrategyStep BeforeCandles(DateTimeOffset moment)
    {
        return new StrategyRun(_strategy, _graph).Read(moment);
    }
}
