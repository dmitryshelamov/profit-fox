using ProfitFox.Application.Ideas;
using ProfitFox.Application.Market.Queries.GetMarket;
using ProfitFox.Application.MarketData;

namespace ProfitFox.Presentation.Contracts.Market;

/// <summary>
///     A page of the market snapshot with the state of the scanner; <c>restPausedUntil</c> only while the pause lasts. <c>page</c> is the number of
///     the page from 1 (the last page when the one asked for is past the end) and <c>total</c> the count of the pairs that meet the query.
///     <c>pairs</c> is the count of the whole snapshot and <c>categories</c> the counts of its categories; <c>setups</c> holds, by setup code and
///     <c>all</c>, the pairs that have the setup on the interval among those left by the category, the search and the saved filter.
/// </summary>
public sealed record MarketResponse(
    DateTimeOffset? SnapshotAt,
    MarketHistoryResponse History,
    DateTimeOffset? RestPausedUntil,
    IReadOnlyList<MarketRowResponse> Items,
    int Page,
    int PageSize,
    int Total,
    int Pairs,
    IReadOnlyDictionary<string, int> Categories,
    IReadOnlyDictionary<string, int> Setups)
{
    public static MarketResponse Of(GetMarketResult result)
    {
        return new MarketResponse(
            result.State.SnapshotAt,
            new MarketHistoryResponse(result.State.PairsBackfilled, result.State.PairsTotal),
            result.State.RestPausedUntil,
            [.. result.Page!.Items.Select(row => MarketRowResponse.Of(row, result.Setups.GetValueOrDefault(row.Instrument) ?? []))],
            result.Page.Number,
            result.Page.Size,
            result.Page.Total,
            result.Counts!.Pairs,
            result.Counts.Categories.ToDictionary(count => count.Key.ToCode(), count => count.Value),
            new Dictionary<string, int>([new KeyValuePair<string, int>("all", result.Counts.Filtered), .. result.Counts.Setups.Select(count => new KeyValuePair<string, int>(count.Key.ToCode(), count.Value))]));
    }
}