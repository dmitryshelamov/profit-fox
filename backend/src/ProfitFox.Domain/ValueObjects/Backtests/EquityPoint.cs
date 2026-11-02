namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     A point of the equity curve: a group of adjacent closes, its end, the equity at the end and the lowest equity in it, with the
///     open time and open of the first candle of the group, its highest high, its lowest low and the close of the last candle.
/// </summary>
public sealed record EquityPoint(
    DateTimeOffset At,
    decimal Equity,
    decimal MinEquity,
    DateTimeOffset OpenTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close);