namespace ProfitFox.Domain.Entities.IdeaTrades;

/// <summary>
///     The one set of settings of trading by ideas: the account to enter on, the risk of a trade, the largest amount of a trade, the most
///     trades open and their total risk, and the lowest reward to the risk after the fees.
/// </summary>
public sealed record IdeaTradingSettings(Guid? AccountId, decimal Risk, decimal MaxAmount, int MaxOpenTrades, decimal MaxTotalRisk, decimal MinRiskReward)
{
    public static readonly IdeaTradingSettings Default = new IdeaTradingSettings(null, 1m, 200m, 5, 5m, 1.5m);
}