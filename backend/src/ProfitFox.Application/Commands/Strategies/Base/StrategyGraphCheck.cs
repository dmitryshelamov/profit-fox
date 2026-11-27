using ProfitFox.Application.Contracts.Strategies;
using ProfitFox.Domain.ValueObjects.Graph;

namespace ProfitFox.Application.Commands.Strategies.Base;

/// <summary>
///     A graph checked as a strategy with the saved signals it points at: the graph as written when it has the right form,
///     every error, what each signal node points at, and the strategy when there are no errors.
/// </summary>
internal sealed record StrategyGraphCheck(
    GraphDefinition? Graph,
    IReadOnlyList<GraphError> Errors,
    IReadOnlyList<SignalUseInternal> Signals,
    TradingStrategy? Strategy);
