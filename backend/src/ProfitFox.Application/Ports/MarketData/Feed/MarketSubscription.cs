namespace ProfitFox.Application.Ports.MarketData.Feed;

/// <summary>
///     What a subscription asks: where to catch up from (null for live only), whether the minute that goes on comes too, and
///     whether only the minutes are wanted, as for the live candle of a page.
/// </summary>
public sealed record MarketSubscription(DateTimeOffset? Since, bool Forming = false, bool MinutesOnly = false);
