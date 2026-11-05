namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     The money of an idea on 1 USDT of the amount of a deal: the profit at the target and the loss at the stop after the fees, and the risk,
///     the distance from the entry to the stop, all in shares of the entry.
/// </summary>
public sealed record IdeaShares(decimal ProfitPerUsdt, decimal LossPerUsdt, decimal RiskPerUsdt);