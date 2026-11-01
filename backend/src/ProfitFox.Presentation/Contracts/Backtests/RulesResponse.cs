namespace ProfitFox.Presentation.Contracts.Backtests;

public sealed record RulesResponse(string TickSize, string QuantityStep, string MinQuantity, string MinAmount);