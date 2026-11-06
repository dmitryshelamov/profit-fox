namespace ProfitFox.Domain.ValueObjects.IdeaTrades;

public sealed record IdeaTradeSize(decimal Quantity, decimal Amount, decimal Risk, decimal Profit, decimal RiskReward);