namespace ProfitFox.Application.Ports.MarketData;

/// <summary>
///     What a read of recent history gave (change add-ideas-and-market, design.md, decision 4): the items, oldest first, or
///     <see cref="Loading" /> — the service is still loading the history of the pair, and the read is to be asked again later.
/// </summary>
public sealed record RecentRead<T>(bool Loading, IReadOnlyList<T> Items)
{
    public static RecentRead<T> StillLoading { get; } = new RecentRead<T>(Loading: true, []);
}
