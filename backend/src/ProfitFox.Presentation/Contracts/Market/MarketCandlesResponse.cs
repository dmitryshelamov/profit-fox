using ProfitFox.Application.Market.Queries.GetMarketCandles;
using ProfitFox.Application.MarketData;
using ProfitFox.Presentation.Contracts.MarketData;

namespace ProfitFox.Presentation.Contracts.Market;

/// <summary>Closed candles of a pair, oldest first; <see cref="Unavailable" /> says Bybit could not fill the period.</summary>
public sealed record MarketCandlesResponse(string Interval, IReadOnlyList<CandleResponse> Candles, bool Unavailable)
{
    public static MarketCandlesResponse Of(GetMarketCandlesResult result)
    {
        return new MarketCandlesResponse(result.Interval.ToCode(), [.. result.Candles!.Select(CandleResponse.Of)], result.Unavailable);
    }
}