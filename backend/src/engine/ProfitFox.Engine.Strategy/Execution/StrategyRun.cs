using ProfitFox.Engine.Strategy.Graph;
using ProfitFox.Engine.Strategy.Nodes;

namespace ProfitFox.Engine.Strategy.Execution;

/// <summary>
///     A strategy running on the closed candles of one pair: the graph is computed once per candle, then the inputs of the
///     position nodes are read. A new pair is a new run.
/// </summary>
internal sealed class StrategyRun
{
    private readonly GraphRun _run;
    private readonly (InputSource When, EntrySide Side)[] _entries;
    private readonly InputSource _after;
    private readonly InputSource? _stop;
    private readonly InputSource? _target;

    public StrategyRun(TradingStrategy strategy, CompiledGraph graph)
    {
        _run = new GraphRun(graph);
        _entries = [.. strategy.Entries.Select(entry => (Input(graph, node => node.Definition.Id == entry.Node)!.Value, entry.Side))];
        _after = Input(graph, node => node.Definition.Kind == NodeCatalog.CloseAfter)!.Value;
        _stop = Input(graph, node => node.Definition.Kind == NodeCatalog.StopLoss);
        _target = Input(graph, node => node.Definition.Kind == NodeCatalog.TakeProfit);
    }

    public StrategyStep Step(ClosedCandle candle, FundingPayment? lastFunding)
    {
        _run.Step(candle, lastFunding);

        return Read(candle.CloseTime, Entry());
    }

    /// <summary>
    ///     What the position nodes hold now, without an order: before the first candle a written value is known and a wired
    ///     one is missing.
    /// </summary>
    public StrategyStep Read(DateTimeOffset moment, EntrySide? entry = null)
    {
        return new StrategyStep(moment, entry, _run.Read(_after).Duration, Distance(_stop), Distance(_target));
    }

    // Entries of different sides at one candle give no order: the graph did not say which to open.
    private EntrySide? Entry()
    {
        EntrySide? side = null;

        foreach ((InputSource when, EntrySide entry) in _entries)
        {
            if (!_run.Read(when).Happened)
            {
                continue;
            }

            if (side is not null && side != entry)
            {
                return null;
            }

            side = entry;
        }

        return side;
    }

    private ExitDistance Distance(InputSource? source)
    {
        return source is { } input ? ExitDistance.Of(_run.Read(input).Number) : ExitDistance.None;
    }

    // The only input of the position node that matches, or null when the strategy has no such node.
    private static InputSource? Input(CompiledGraph graph, Func<CompiledNode, bool> match)
    {
        return graph.Nodes.FirstOrDefault(match)?.Inputs[0];
    }
}
