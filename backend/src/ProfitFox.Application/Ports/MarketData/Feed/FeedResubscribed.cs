namespace ProfitFox.Application.Ports.MarketData.Feed;

/// <summary>
///     A feed subscribed again after a break (task 26.2; spec market-live-feed «Догон после разрыва»): what it tells next is
///     first a replay from <see cref="Since" /> — told by minute and within a minute by instrument, as a catch-up — and only
///     then the live feed again. Told before the first item of the new subscription; the first subscription tells none.
/// </summary>
public sealed record FeedResubscribed(string Feed, DateTimeOffset? Since) : MarketEvent;
