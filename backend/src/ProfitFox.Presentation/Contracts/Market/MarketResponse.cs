using ProfitFox.Application.Market.Queries.GetMarket;

namespace ProfitFox.Presentation.Contracts.Market;

/// <summary>The market snapshot with the state of the scanner; <see cref="RestPausedUntil" /> only while the pause lasts.</summary>
public sealed record MarketResponse(DateTimeOffset? SnapshotAt, MarketHistoryResponse History, DateTimeOffset? RestPausedUntil, IReadOnlyList<MarketRowResponse> Rows)
{
    public static MarketResponse Of(GetMarketResult result)
    {
        return new MarketResponse(
            result.State.SnapshotAt,
            new MarketHistoryResponse(result.State.PairsBackfilled, result.State.PairsTotal),
            result.State.RestPausedUntil,
            [.. result.Rows.Select(row => MarketRowResponse.Of(row, result.Setups.GetValueOrDefault(row.Instrument) ?? []))]);
    }
}