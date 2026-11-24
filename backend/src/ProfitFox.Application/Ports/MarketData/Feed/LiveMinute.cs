namespace ProfitFox.Application.Ports.MarketData.Feed;

/// <summary>
///     A minute candle of a pair: prices, the volume in coins and the turnover in USDT — the volume times the close when the
///     service does not tell it. A minute not <see cref="Closed" /> is the one that goes on; a minute may come again, and the
///     later one replaces the earlier by pair and open time.
/// </summary>
public sealed record LiveMinute(
    string Symbol,
    DateTimeOffset OpenTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    decimal Turnover,
    bool Closed = true) : MarketEvent;
