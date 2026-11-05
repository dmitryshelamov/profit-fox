namespace ProfitFox.Domain.ValueObjects.Market;

/// <summary>
///     A closed minute candle as the scanner keeps it: its open time, prices and turnover in the quote coin. The metrics use open, close and
///     turnover; the ideas also the high and low.
/// </summary>
public sealed record MarketMinute(DateTimeOffset OpenTime, decimal Open, decimal High, decimal Low, decimal Close, decimal Turnover);