using ProfitFox.Application.Queries.Market;
using ProfitFox.Application.Services.Market;
using ProfitFox.Domain.ValueObjects.Lists;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Presentation.Contracts.Market.Converters;

/// <summary>
///     The market as the API sends it.
/// </summary>
public static class MarketResponses
{
    public static MarketPairResponse Of(MarketPairRow row)
    {
        return new MarketPairResponse(
            row.Pair.Symbol,
            (double)row.Pair.Price,
            (double?)row.Pair.Change1h,
            (double?)row.Pair.Change4h,
            (double?)row.Pair.Change24h,
            (double?)row.Pair.Turnover24h,
            (double?)row.Pair.OpenInterest,
            (double?)row.Pair.OpenInterestUsdt,
            (double?)row.Pair.FundingRate,
            row.Pair.NextFunding?.ToUnixTimeMilliseconds(),
            row.Pair.UpdatedAt.ToUnixTimeMilliseconds(),
            [.. row.Ideas.Select(idea => new IdeaBadgeResponse(idea.Strategy, idea.Timeframe, idea.Side))]);
    }

    public static MarketPairResponse Of(MarketPairInternal pair)
    {
        return pair.Row is { } row ? Of(row) : new MarketPairResponse(pair.Pair.Symbol, null, null, null, null, null, null, null, null, null, null, []);
    }

    public static MarketPageResponse Of(MarketPairsResult result)
    {
        return new MarketPageResponse([.. result.Page.Items.Select(Of)], result.Page.Total, result.Page.Page, result.Page.PageSize, result.FilterGone, result.UpdatedAt?.ToUnixTimeMilliseconds());
    }

    public static PairChartResponse Of(string symbol, PairChartInternal chart)
    {
        return new PairChartResponse(
            symbol,
            Timeframes.Name(chart.Timeframe),
            chart.Loading,
            [
                .. chart.Candles.Where(candle => candle.HasTrades).Select(candle => candle.ToClosedCandle(chart.Timeframe)).Select(candle => new ChartCandle(
                    candle.OpenTime.ToUnixTimeMilliseconds(),
                    (double)candle.Open,
                    (double)candle.High,
                    (double)candle.Low,
                    (double)candle.Close,
                    (double)candle.Volume))
            ],
            [.. chart.Funding.Select(payment => payment.ToFundingPayment()).Select(payment => new ChartPoint(payment.Time.ToUnixTimeMilliseconds(), (double)payment.Rate))]);
    }

    public static PairOpenInterestResponse Of(string symbol, PairOpenInterestInternal interest)
    {
        return new PairOpenInterestResponse(
            symbol,
            Timeframes.Name(interest.Timeframe),
            interest.Loading,
            [.. interest.OpenInterest.Select(point => new ChartPoint(point.Time.ToUnixTimeMilliseconds(), (double)point.Value))]);
    }

    public static LiveCandleResponse Of(LiveCandle candle)
    {
        return new LiveCandleResponse(
            candle.OpenTime.ToUnixTimeMilliseconds(),
            (double)candle.Open,
            (double)candle.High,
            (double)candle.Low,
            (double)candle.Close,
            (double)candle.Volume,
            candle.Closed,
            candle.UpdatedAt.ToUnixTimeMilliseconds());
    }
}
