using ProfitFox.Engine.Strategy.Graph;

namespace ProfitFox.Engine.Strategy.Execution;

/// <summary>
///     A signal running on many pairs: one <see cref="GraphRun" /> per pair, so the state of one pair never reaches another
///     and the order in which pairs come does not matter (spec strategy-execution).
/// </summary>
public sealed class SignalExecutor
{
    private readonly CompiledGraph _graph;
    private readonly Dictionary<string, GraphRun> _runs = new Dictionary<string, GraphRun>(StringComparer.Ordinal);

    public SignalExecutor(Signal signal)
    {
        _graph = CompiledGraph.Compile(signal.Graph);
    }

    /// <summary>
    ///     The event of the pair at the close of <paramref name="candle" />, or null. Candles of one pair go in the order of
    ///     their close; pairs may be mixed in any order.
    /// </summary>
    public SignalEvent? Step(string instrument, ClosedCandle candle, FundingPayment? lastFunding)
    {
        if (!_runs.TryGetValue(instrument, out GraphRun? run))
        {
            run = new GraphRun(_graph);
            _runs.Add(instrument, run);
        }

        return run.Step(candle, lastFunding)[0].Happened ? new SignalEvent(instrument, candle.CloseTime) : null;
    }
}
