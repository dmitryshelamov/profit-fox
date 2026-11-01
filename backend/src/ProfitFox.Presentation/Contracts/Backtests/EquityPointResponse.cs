namespace ProfitFox.Presentation.Contracts.Backtests;

/// <summary>The end of a group of closes, the equity at it and the lowest equity in the group.</summary>
public sealed record EquityPointResponse(DateTimeOffset At, string Equity, string MinEquity);