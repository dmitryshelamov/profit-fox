namespace ProfitFox.Presentation.Contracts.Backtests;

/// <summary>
///     The end of a group of closes, the equity at it and the lowest equity in the group, with the open time and open of the first
///     candle of the group, its highest high, its lowest low and the close of the last candle.
/// </summary>
public sealed record EquityPointResponse(DateTimeOffset At, string Equity, string MinEquity, DateTimeOffset OpenTime, string Open, string High, string Low, string Close);