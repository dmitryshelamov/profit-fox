using ProfitFox.Application.Ports.MarketData.Feed;

namespace ProfitFox.Application.Ports.MarketData;

/// <summary>
///     The live feeds of the market-data service (change add-ideas-and-market, design.md, decision 2): minute candles,
///     funding payments, funding rates and open interest of the listed linear USDT perpetuals. The feed subscribes again by
///     itself after a break, tells a new subscription of minutes before its replay (<see cref="FeedResubscribed" />) and a hole
///     it cannot catch up (<see cref="FeedGap" />); it ends only when cancelled.
/// </summary>
public interface IMarketFeed
{
    /// <summary>
    ///     What the feeds tell from <see cref="MarketSubscription.Since" />, in the order it comes, first the set of pairs
    ///     (<see cref="MarketPairs" />) and again whenever it changes. The events are read into a queue of their own, so a
    ///     slow reader does not get the subscription cut off.
    /// </summary>
    IAsyncEnumerable<MarketEvent> Subscribe(MarketSubscription subscription, CancellationToken cancellation);
}
