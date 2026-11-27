using ProfitFox.Domain.Services.Nodes;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;

namespace ProfitFox.Domain.Services.Execution;

/// <summary>
///     A strategy running on the closed candles of one pair: the graph is computed once per candle, then the inputs of the
///     position nodes are read. A new pair is a new run.
/// </summary>
internal sealed class StrategyRun
{
    private readonly GraphRun _run;
    private readonly (InputSource When, InputSource Size, EntrySide Side, string Node)[] _entries;
    private readonly InputSource? _after;
    private readonly InputSource? _stop;
    private readonly InputSource? _target;

    public StrategyRun(TradingStrategy strategy, CompiledGraph graph)
    {
        _run = new GraphRun(graph);
        _entries =
        [
            .. strategy.Entries.Select(entry => (
                Input(graph, node => node.Definition.Id == entry.Node, NodeCatalog.When)!.Value,
                Input(graph, node => node.Definition.Id == entry.Node, NodeCatalog.Size)!.Value,
                entry.Side,
                entry.Node))
        ];
        _after = Input(graph, node => node.Definition.Kind == NodeCatalog.CloseAfter, NodeCatalog.After);
        _stop = Input(graph, node => node.Definition.Kind == NodeCatalog.StopLoss, NodeCatalog.Distance);
        _target = Input(graph, node => node.Definition.Kind == NodeCatalog.TakeProfit, NodeCatalog.Distance);
    }

    public StrategyStep Step(ClosedCandle candle, CandleFacts facts, IReadOnlyDictionary<string, ClosedCandle>? pairs = null)
    {
        _run.Step(candle, facts, position: null, pairs);

        (EntrySide? side, decimal? size, string? node) = Entry();
        return Read(candle.CloseTime, side) with { Size = side is null ? FirstSize() : size, Node = node };
    }

    /// <summary>
    ///     What the position nodes hold now, without an order: before the first candle a written value is known and a wired
    ///     one is missing.
    /// </summary>
    public StrategyStep Read(DateTimeOffset moment, EntrySide? entry = null)
    {
        return new StrategyStep(moment, entry, _after is { } after ? _run.Read(after).Duration : null, Distance(_stop), Distance(_target)) { Size = FirstSize() };
    }

    // Entries of different sides at one candle give no order: the graph did not say which to open. The size is that of the
    // first node whose event happened.
    private (EntrySide? Side, decimal? Size, string? Node) Entry()
    {
        EntrySide? side = null;
        decimal? size = null;
        string? node = null;

        foreach ((InputSource when, InputSource sizeSource, EntrySide entry, string id) in _entries)
        {
            if (!_run.Read(when).Happened)
            {
                continue;
            }

            if (side is not null && side != entry)
            {
                return (null, null, null);
            }

            size = side is null ? _run.Read(sizeSource).Number : size;
            node ??= id;
            side = entry;
        }

        return (side, size, node);
    }

    private decimal? FirstSize()
    {
        return _entries.Length > 0 ? _run.Read(_entries[0].Size).Number : null;
    }

    private ExitDistance Distance(InputSource? source)
    {
        return source is { } input ? ExitDistance.Of(_run.Read(input).Number) : ExitDistance.None;
    }

    // The input of the first node that matches, or null when the strategy has no such node.
    private static InputSource? Input(CompiledGraph graph, Func<CompiledNode, bool> match, string input)
    {
        if (graph.Nodes.FirstOrDefault(match) is not { } node)
        {
            return null;
        }

        int index = node.Kind.Inputs(graph.Graph).Select(pin => pin.Name).ToList().IndexOf(input);
        return node.Inputs[index];
    }
}
