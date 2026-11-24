namespace ProfitFox.Application.Ports.MarketData.Feed;

/// <summary>
///     What a subscription asks: where to catch up from (null for live only), whether the minute that goes on comes too,
///     whether only the minutes are wanted, as for the live candle of a page, and — without <see cref="Since" /> — whether
///     to catch up the whole day the service holds, counted back from the moment each feed subscribes (task 18.9): a hole
///     is then never told for the start.
/// </summary>
public sealed record MarketSubscription(DateTimeOffset? Since, bool Forming = false, bool MinutesOnly = false, bool WholeDay = false);
