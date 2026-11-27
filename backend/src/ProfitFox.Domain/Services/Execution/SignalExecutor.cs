using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph;

namespace ProfitFox.Domain.Services.Execution;

/// <summary>
///     A signal running on many pairs: one <see cref="GraphRun" /> per pair, so the state of one pair never reaches another
///     and the order in which pairs come does not matter (spec strategy-execution). Each call moves the run of its pair by
///     one candle: a pair goes either by <see cref="Step" /> or by <see cref="Value" />.
/// </summary>
public sealed class SignalExecutor
{
    private readonly CompiledGraph _graph;
    private readonly bool _state;
    private readonly Dictionary<string, PairRun> _runs = new Dictionary<string, PairRun>(StringComparer.Ordinal);

    public SignalExecutor(Signal signal)
    {
        _graph = CompiledGraph.Compile(signal.Graph);
        _state = signal.IsState;
    }

    /// <summary>
    ///     The event of the pair at the close of <paramref name="candle" />, or null. A signal-state has its event where its
    ///     value turns true after false or missing (spec signal-preview «Предпросмотр сигнала-состояния»). Candles of one pair
    ///     go in the order of their close; pairs may be mixed in any order.
    /// </summary>
    public SignalEvent? Step(string instrument, ClosedCandle candle, CandleFacts facts, IReadOnlyDictionary<string, ClosedCandle>? pairs = null)
    {
        PairRun run = Run(instrument);
        bool? before = run.Last;
        bool? value = Advance(run, candle, facts, pairs);
        bool happened = _state ? value == true && before != true : value == true;
        return happened ? new SignalEvent(instrument, candle.CloseTime) : null;
    }

    /// <summary>
    ///     The value of the output of the pair at the close of <paramref name="candle" /> (spec strategy-execution «Выход
    ///     сигнала-состояния»): true, false or missing for a signal-state, whether the event happened for a signal of an event.
    /// </summary>
    public bool? Value(string instrument, ClosedCandle candle, CandleFacts facts, IReadOnlyDictionary<string, ClosedCandle>? pairs = null)
    {
        return Advance(Run(instrument), candle, facts, pairs);
    }

    /// <summary>
    ///     Forgets the run of the pair: its next candle starts a run from nothing (task 18.8).
    /// </summary>
    public void Forget(string instrument)
    {
        _runs.Remove(instrument);
    }

    private PairRun Run(string instrument)
    {
        if (!_runs.TryGetValue(instrument, out PairRun? run))
        {
            run = new PairRun(new GraphRun(_graph));
            _runs.Add(instrument, run);
        }

        return run;
    }

    private static bool? Advance(PairRun run, ClosedCandle candle, CandleFacts facts, IReadOnlyDictionary<string, ClosedCandle>? pairs)
    {
        PinValue output = run.Graph.Step(candle, facts, position: null, pairs)[0];
        run.Last = output.Boolean;
        return output.Boolean;
    }

    // The run of one pair and the last value of its output.
    private sealed class PairRun(GraphRun graph)
    {
        public GraphRun Graph { get; } = graph;

        public bool? Last { get; set; }
    }
}
