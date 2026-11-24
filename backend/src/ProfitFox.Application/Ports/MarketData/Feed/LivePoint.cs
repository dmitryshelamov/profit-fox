namespace ProfitFox.Application.Ports.MarketData.Feed;

/// <summary>
///     A value of a pair at a time from the feed of its <see cref="Kind" />: a funding payment with its rate, a funding rate
///     of the coming payment, or the open interest in coins.
/// </summary>
public sealed record LivePoint(LivePointKind Kind, string Symbol, DateTimeOffset Time, decimal Value) : MarketEvent;
