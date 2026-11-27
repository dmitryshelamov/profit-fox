namespace ProfitFox.Application.Ports.MarketData.Feed;

/// <summary>
///     A hole in the live data of a feed (spec market-live-feed «Догон после разрыва»): what the feed told in
///     [<see cref="From" />, <see cref="Until" />) cannot be caught up, the break was longer than the service catches up.
/// </summary>
public sealed record FeedGap(string Feed, DateTimeOffset From, DateTimeOffset Until) : MarketEvent;
