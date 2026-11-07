namespace ProfitFox.Presentation.Contracts.IdeaTrades;

/// <summary>The settings of trading by ideas: the account to enter on, the amount and the largest amount of a trade in USDT, the most open trades and their total risk, the lowest reward to the risk after the fees.</summary>
public sealed class IdeaTradingSettingsBody
{
    public Guid? AccountId { get; init; }

    public decimal Amount { get; init; }

    public decimal MaxAmount { get; init; }

    public int MaxOpenTrades { get; init; }

    public decimal MaxTotalRisk { get; init; }

    public decimal MinRiskReward { get; init; }

    /// <summary>The loss of a day in USDT that stops the autos with money; none switches the guard off.</summary>
    public decimal? DailyLossLimit { get; init; }

    /// <summary>The most open real trades of one side that the autos with money stand; none switches the guard off.</summary>
    public int? MaxSameDirection { get; init; }

    /// <summary>The most the open real trades of all the autos may hold in USDT; none switches the guard off.</summary>
    public decimal? AutosBudget { get; init; }
}