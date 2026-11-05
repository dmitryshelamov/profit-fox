namespace ProfitFox.Presentation.Contracts.IdeaTrades;

/// <summary>The settings of trading by ideas: the account to enter on, the risk and the largest amount of a trade in USDT, the most open trades and their total risk, the lowest reward to the risk after the fees.</summary>
public sealed class IdeaTradingSettingsBody
{
    public Guid? AccountId { get; init; }

    public decimal Risk { get; init; }

    public decimal MaxAmount { get; init; }

    public int MaxOpenTrades { get; init; }

    public decimal MaxTotalRisk { get; init; }

    public decimal MinRiskReward { get; init; }
}