namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     The money of an idea on 1 USDT of the amount of a deal: the profit at the target and the loss at the stop after the fees, and the risk,
///     the distance from the entry to the stop, all in shares of the entry.
///     <see cref="NetRiskReward" /> is the profit over the loss, the reward to risk after the fees. The loss holds the distance to the stop and
///     the fees, so it is never zero.
/// </summary>
public sealed record IdeaShares(decimal ProfitPerUsdt, decimal LossPerUsdt, decimal RiskPerUsdt)
{
    public decimal NetRiskReward => ProfitPerUsdt / LossPerUsdt;
}