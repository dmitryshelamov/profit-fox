namespace ProfitFox.Domain.Entities.IdeaTrades;

/// <summary>
///     The one set of settings of trading by ideas: the account to enter on, the amount of a trade, the largest amount of a trade, the most
///     trades open and their total risk, and the lowest reward to the risk after the fees. The two guards of the autos with money are none while
///     off: the loss of a day in USDT that stops them, the most open trades of one side, and the sum of the amounts of the open real trades of all
///     the autos (<see cref="AutosBudget" />).
/// </summary>
public sealed record IdeaTradingSettings(
    Guid? AccountId,
    decimal Amount,
    decimal MaxAmount,
    int MaxOpenTrades,
    decimal MaxTotalRisk,
    decimal MinRiskReward,
    decimal? DailyLossLimit = null,
    int? MaxSameDirection = null,
    decimal? AutosBudget = null)
{
    public static readonly IdeaTradingSettings Default = new IdeaTradingSettings(null, 20m, 200m, 5, 5m, 1.5m);
}