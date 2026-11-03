using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Ports.MarketData;

/// <summary>
///     An update of a candle that may still be forming: only for showing, never stored and never given to a strategy.
///     <see cref="ObservedAt" /> is the time of the update at the exchange; <see cref="IsClosed" /> says the interval has ended.
/// </summary>
public sealed record FormingCandle(
    DateTimeOffset OpenTime,
    Price Open,
    Price High,
    Price Low,
    Price Close,
    Volume Volume,
    bool IsClosed,
    DateTimeOffset ObservedAt);