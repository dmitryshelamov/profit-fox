using MediatR;

using ProfitFox.Application.Commands.Strategies.Base;
using ProfitFox.Application.Contracts.Backtests.Converters;
using ProfitFox.Application.Contracts.Strategies;
using ProfitFox.Application.Contracts.Strategies.Converters;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.Nodes;
using ProfitFox.Domain.ValueObjects.Graph;

namespace ProfitFox.Application.Queries.Strategies;

/// <summary>
///     The names and the latest versions of the signals, and the last backtest of each strategy, are read once for all the
///     strategies of the list.
/// </summary>
internal sealed class GetStrategiesQueryHandler(
    IStrategyRepository strategies,
    ISignalRepository signals,
    IBacktestRepository backtests,
    IStrategyIdeasRepository? ideas = null)
    : IRequestHandler<GetStrategiesQuery, IReadOnlyList<StrategyEntryInternal>>
{
    // The kinds that close a position, in the order of the list: stop, target, time, close by a condition, limit order. A
    // market order is how a strategy without a signal enters, so it is not an exit here (change add-position-node, task 6.2).
    private static readonly string[] ExitKinds =
        [NodeCatalog.StopLoss, NodeCatalog.TakeProfit, NodeCatalog.CloseAfter, NodeCatalog.ClosePosition, NodeCatalog.LimitOrder];

    public async Task<IReadOnlyList<StrategyEntryInternal>> Handle(GetStrategiesQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, Backtest> last = await backtests.LastOfStrategiesAsync(cancellationToken);
        Dictionary<Guid, StrategyIdeas> enabled = ideas is null ? [] : (await ideas.AllAsync(cancellationToken)).ToDictionary(item => item.StrategyId);
        List<(StrategyEntryInternal Entry, GraphDefinition? Graph)> read = [];
        foreach (string name in (await strategies.NamesAsync(cancellationToken)).Order(StringComparer.Ordinal))
        {
            read.Add(await Entry(name, last, enabled, cancellationToken));
        }

        SignalReference[] references = [.. read.SelectMany(item => item.Graph is null ? [] : SignalNodes.References(item.Graph)).Distinct()];
        IReadOnlyList<SignalVersion> versions = references.Length == 0 ? [] : await signals.ReadVersionsAsync(references, cancellationToken);
        var found = versions.ToDictionary(version => version.Reference);
        return [.. read.Select(item => item.Graph is null ? item.Entry : item.Entry with { Signals = Signals(item.Graph, found), Exits = Exits(item.Graph) })];
    }

    private async Task<(StrategyEntryInternal Entry, GraphDefinition? Graph)> Entry(
        string name,
        IReadOnlyDictionary<Guid, Backtest> last,
        Dictionary<Guid, StrategyIdeas> enabled,
        CancellationToken cancellationToken)
    {
        try
        {
            StrategyDocument strategy = await StrategyChecks.ReadAsync(strategies, name, cancellationToken);
            return (strategy.ToEntryInternal() with { LastRun = last.GetValueOrDefault(strategy.Id)?.ToLastInternal(), Ideas = enabled.GetValueOrDefault(strategy.Id) }, strategy.Graph);
        }
        catch (StrategyLibraryException exception)
        {
            return (new StrategyEntryInternal(name, Description: null, Version: 0, exception.Message), null);
        }
    }

    // A reference whose signal is not saved has no name to show. The database does not let it happen: a signal in use is not deleted.
    private static IReadOnlyList<StrategySignalInternal> Signals(GraphDefinition graph, Dictionary<SignalReference, SignalVersion> found)
    {
        StrategySignalInternal[] uses =
        [
            .. SignalNodes.References(graph)
                .Where(found.ContainsKey)
                .Select(reference => new StrategySignalInternal(found[reference].Name, reference.Version, found[reference].LatestVersion))
                .OrderBy(use => use.Name, StringComparer.Ordinal)
                .ThenBy(use => use.Version)
        ];
        return uses.Length == 0 ? [] : uses;
    }

    private static IReadOnlyList<string> Exits(GraphDefinition graph)
    {
        string[] exits = [.. ExitKinds.Where(kind => graph.Nodes.Any(node => node.Kind == kind))];
        return exits.Length == 0 ? [] : exits;
    }
}
