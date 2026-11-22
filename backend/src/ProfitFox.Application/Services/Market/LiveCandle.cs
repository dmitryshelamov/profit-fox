namespace ProfitFox.Application.Services.Market;

/// <summary>
///     The candle of a timeframe a page of a pair draws live (change add-ideas-and-market, spec market-overview «Живая
///     свеча»): built from the minutes of its window, the one that goes on included; <see cref="Closed" /> once the clock
///     passed its end. <see cref="UpdatedAt" /> is when it was sent.
/// </summary>
public sealed record LiveCandle(
    DateTimeOffset OpenTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    bool Closed,
    DateTimeOffset UpdatedAt);
