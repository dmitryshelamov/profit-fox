namespace ProfitFox.Domain.ValueObjects.IdeaTrades;

/// <summary>The target and the stop the exchange stands for an entry.</summary>
public sealed record Protection(decimal TakeProfit, decimal StopLoss);