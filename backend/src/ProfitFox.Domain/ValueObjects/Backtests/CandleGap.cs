namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>A part <c>[From, To)</c> of the period that has no candles even after loading.</summary>
public sealed record CandleGap(DateTimeOffset From, DateTimeOffset To);