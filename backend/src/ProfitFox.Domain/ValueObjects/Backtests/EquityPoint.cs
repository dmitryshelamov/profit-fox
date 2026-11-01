namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>A point of the equity curve: a group of adjacent closes, its end, the equity at the end and the lowest equity in it.</summary>
public sealed record EquityPoint(DateTimeOffset At, decimal Equity, decimal MinEquity);