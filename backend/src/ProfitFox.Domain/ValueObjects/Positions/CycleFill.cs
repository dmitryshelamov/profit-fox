namespace ProfitFox.Domain.ValueObjects.Positions;

/// <summary>
///     A fill of a cycle: its moment, what it did to the position, its side, the price it filled at with the slippage, the
///     quantity in coins, the amount in USDT and the node of the order — null for a cycle opened outside the graph.
/// </summary>
public sealed record CycleFill(DateTimeOffset Moment, FillAction Action, FillSide Side, double Price, double Quantity, double Amount, string? Node);
