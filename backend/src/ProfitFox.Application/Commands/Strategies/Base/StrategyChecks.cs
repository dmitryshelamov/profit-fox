using System.Text.Json;

using ProfitFox.Application.Commands.Base;
using ProfitFox.Application.Contracts.Strategies;
using ProfitFox.Application.Contracts.Strategies.Converters;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.Signals;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Signals;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;

namespace ProfitFox.Application.Commands.Strategies.Base;

/// <summary>
///     What the requests of the strategy library (spec strategy-constructor) check the same way. A graph is checked as a
///     strategy with the versions of signals it points at, read by one request (change add-strategy-constructor, design.md,
///     decision 2); it is written only without errors, and a name only when it follows the rule of names.
/// </summary>
internal static class StrategyChecks
{
    /// <summary>
    ///     The graph checked as a strategy. The references are read from its form first; a graph of the wrong form has only the
    ///     errors of its form.
    /// </summary>
    public static Task<StrategyGraphCheck> CheckAsync(ISignalRepository signals, JsonElement graph, CancellationToken cancellationToken)
    {
        return CheckAsync(signals, graph, StrategyKind.Bot, cancellationToken);
    }

    /// <summary>
    ///     The graph checked as a strategy of <paramref name="kind" />: the checks of a strategy and the rules of the kind
    ///     (spec strategy-graph «Вид стратегии»).
    /// </summary>
    public static async Task<StrategyGraphCheck> CheckAsync(ISignalRepository signals, JsonElement graph, StrategyKind kind, CancellationToken cancellationToken)
    {
        ReadResult<GraphDefinition> form = StrategyGraph.ReadForm(graph);
        if (form.Value is not { } definition)
        {
            return new StrategyGraphCheck(Graph: null, form.Errors, [], Strategy: null);
        }

        SignalSet set = await SignalsOf(signals, definition, cancellationToken);
        ReadResult<TradingStrategy> read = StrategyGraph.ReadStrategy(graph, set, kind);
        return new StrategyGraphCheck(definition, read.Errors, definition.ToSignalUses(set), read.Value);
    }

    /// <summary>
    ///     What each signal node of a saved graph points at now: the current names and latest versions of the signals.
    /// </summary>
    public static async Task<IReadOnlyList<SignalUseInternal>> UsesAsync(ISignalRepository signals, GraphDefinition graph, CancellationToken cancellationToken)
    {
        return graph.ToSignalUses(await SignalsOf(signals, graph, cancellationToken));
    }

    /// <summary>
    ///     The strategy of a request, with the positions of the nodes of its graph only, and what its signal nodes point at.
    ///     The graph is checked by the rules of <paramref name="kind" />; without a kind the strategy takes the kind of its
    ///     graph, as one saved before strategies had kinds does.
    /// </summary>
    /// <exception cref="StrategyLibraryException">The name is not allowed or the graph has errors.</exception>
    public static async Task<(StrategyDocument Strategy, IReadOnlyList<SignalUseInternal> Signals)> CheckedAsync(
        ISignalRepository signals,
        string name,
        string? description,
        JsonElement graph,
        IReadOnlyDictionary<string, NodePosition> layout,
        StrategyKind? kind,
        CancellationToken cancellationToken)
    {
        List<GraphError> errors = [];
        if (!SignalNames.IsValid(name))
        {
            errors.Add(NameError(name));
        }

        StrategyKind checkedKind = kind ?? (StrategyGraph.ReadForm(graph).Value is { } form ? StrategyKindRules.Of(StrategyUpgrade.Raise(form)) : StrategyKind.Bot);
        StrategyGraphCheck check = await CheckAsync(signals, graph, checkedKind, cancellationToken);
        errors.AddRange(check.Errors);
        if (errors.Count > 0 || check.Graph is null)
        {
            throw new StrategyLibraryException(StrategyProblem.Invalid, string.Join("; ", errors), errors);
        }

        var strategy = StrategyDocument.Create(
            name,
            string.IsNullOrWhiteSpace(description) ? null : description,
            check.Graph,
            GraphLayout.Of(check.Graph, layout),
            checkedKind);
        return (strategy, check.Signals);
    }

    /// <summary>
    ///     The latest version of a saved strategy.
    /// </summary>
    /// <exception cref="StrategyLibraryException">There is no such strategy, or it does not read.</exception>
    public static async Task<StrategyDocument> ReadAsync(IStrategyRepository strategies, string name, CancellationToken cancellationToken)
    {
        if (!SignalNames.IsValid(name) || await strategies.ReadAsync(name, cancellationToken) is not { } read)
        {
            throw NotFound(name);
        }

        return read.Strategy ?? throw new StrategyLibraryException(StrategyProblem.Unreadable, string.Join("; ", read.Errors));
    }

    public static GraphError NameError(string name)
    {
        return DocumentNames.Error(name, "strategy");
    }

    public static StrategyLibraryException NotFound(string name)
    {
        return new StrategyLibraryException(StrategyProblem.NotFound, $"there is no strategy {name}");
    }

    public static StrategyLibraryException Taken(string name)
    {
        return new StrategyLibraryException(StrategyProblem.NameTaken, $"the name {name} is taken");
    }

    private static async Task<SignalSet> SignalsOf(ISignalRepository signals, GraphDefinition graph, CancellationToken cancellationToken)
    {
        IReadOnlyList<SignalReference> references = SignalNodes.References(graph);
        return references.Count == 0 ? SignalSet.Empty : new SignalSet(await signals.ReadVersionsAsync(references, cancellationToken));
    }
}
