using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.ValueObjects.Positions;

/// <summary>
///     What a strategy with orders did on a pair: its cycles in the order they opened and its orders in time order.
/// </summary>
public sealed record PositionResult(IReadOnlyList<CycleOutcome> Cycles, IReadOnlyList<OrderOutcome> Orders);
