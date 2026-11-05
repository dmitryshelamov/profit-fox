namespace ProfitFox.Domain.ValueObjects.IdeaTrades;

/// <summary>The limits of the settings that stop an entry: the lowest reward to the risk after the fees, the open trades and their total risk.</summary>
public sealed record IdeaTradingLimits(decimal MinRiskReward, int MaxOpenTrades, decimal MaxTotalRisk);