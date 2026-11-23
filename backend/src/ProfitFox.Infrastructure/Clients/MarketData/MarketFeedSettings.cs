namespace ProfitFox.Infrastructure.Clients.MarketData;

/// <summary>
///     How <see cref="GrpcMarketFeed" /> keeps its subscriptions (change add-ideas-and-market, design.md, decision 2): the
///     pause before it subscribes again, how often it reads the catalog of pairs, how far back the service catches up and
///     how far before the last item it got a new subscription starts.
/// </summary>
public sealed record MarketFeedSettings
{
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan CatalogRefresh { get; init; } = TimeSpan.FromHours(1);

    public TimeSpan CatchUp { get; init; } = TimeSpan.FromHours(24);

    /// <summary>
    ///     A new subscription of minutes, rates and open interest starts this long before the last item it got.
    /// </summary>
    public TimeSpan Overlap { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     A new subscription of payments starts this long before the newest payment it got: the service tells the payments
    ///     of one hour minutes apart and not in the order of their times.
    /// </summary>
    public TimeSpan PaymentOverlap { get; init; } = TimeSpan.FromHours(2);

    /// <summary>
    ///     How many items of the four feeds wait for the reader at most (task 18.12): a full queue stops the reading of the
    ///     streams, and a stream the service cuts off for it (<c>RESOURCE_EXHAUSTED</c>) is subscribed again from its last item.
    /// </summary>
    public int QueueCapacity { get; init; } = 20_000;

    /// <summary>
    ///     How long the stream of minutes may tell nothing (task 24.1): silent for longer, it is broken off and subscribed
    ///     again from a little before its last minute, as after a break. The minutes of the whole market come every minute; the
    ///     time the feed waits for its reader is not silence. The payments, the rates and the open interest are quiet by their
    ///     nature and are not watched. Zero or less — not watched.
    /// </summary>
    public TimeSpan FeedSilence { get; init; } = TimeSpan.FromMinutes(3);
}
