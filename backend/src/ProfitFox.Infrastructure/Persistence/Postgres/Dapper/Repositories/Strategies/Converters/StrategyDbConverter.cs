using System.Text.Json;

using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Signals;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies.Converters;

internal static class StrategyDbConverter
{
    /// <summary>
    ///     The graph of a strategy as the engine writes it: this is what the version of a strategy keeps, with its signal nodes.
    /// </summary>
    public static string WriteGraph(StrategyDocument strategy)
    {
        return StrategyGraph.Write(strategy.Graph);
    }

    public static string WriteLayout(StrategyDocument strategy)
    {
        return LayoutJson.Write(strategy.Layout);
    }

    /// <summary>
    ///     The rows of <c>strategy_signals</c> of version <paramref name="version" />: one for each signal node of the graph.
    /// </summary>
    public static IReadOnlyList<StrategySignalDb> ToSignalRows(Guid id, int version, StrategyDocument strategy)
    {
        List<StrategySignalDb> rows = [];
        foreach (NodeDefinition node in strategy.Graph.Nodes)
        {
            if (SignalNodes.TryReference(node, out SignalReference reference))
            {
                rows.Add(new StrategySignalDb(id, version, node.Id, reference.Signal, reference.Version));
            }
        }

        return rows;
    }

    /// <summary>
    ///     The strategy of a row with the graph of its latest version, or the errors that stop it: its graph does not have the
    ///     form of a graph, or its layout is not one. The meaning of the graph is checked by the application, which knows the
    ///     saved signals.
    /// </summary>
    public static StrategyRead ToDomain(StrategyDb row)
    {
        using var graphDocument = JsonDocument.Parse(row.Graph);
        ReadResult<GraphDefinition> read = StrategyGraph.ReadForm(graphDocument.RootElement);
        List<string> errors = [.. read.Errors.Select(error => $"graph: {error}")];
        Dictionary<string, NodePosition> positions = [];
        using var layoutDocument = JsonDocument.Parse(row.Layout);
        errors.AddRange(LayoutJson.Read(layoutDocument.RootElement, positions));
        return errors.Count == 0 && read.Value is not null
            ? new StrategyRead(StrategyDocument.Restore(row.Id, row.Name, row.Description, read.Value, positions, row.Version), [])
            : new StrategyRead(null, errors);
    }
}
