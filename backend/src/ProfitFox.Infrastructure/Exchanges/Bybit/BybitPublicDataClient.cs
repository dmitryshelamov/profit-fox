using Bybit.Net.Enums;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Objects.Models.V5;

using ProfitFox.Application.Ports.Market;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

using Interval = ProfitFox.Domain.Entities.MarketData.Enums.Interval;
using MarketCategory = ProfitFox.Domain.Entities.MarketData.Enums.MarketCategory;

namespace ProfitFox.Infrastructure.Exchanges.Bybit;

/// <summary>Public Bybit v5 data of the scanner; every request goes through <see cref="ScannerRestGate" />. No API key, never sends orders.</summary>
internal sealed class BybitPublicDataClient(IBybitRestClient bybit, ScannerRestGate gate) : IMarketDataFeed
{
    // The most points Bybit gives in one page of open interest and of funding history.
    private const int HistoryPageSize = 200;

    public Task<IReadOnlyList<ListedInstrument>> ListInstrumentsAsync(MarketCategory category, CancellationToken cancellationToken)
    {
        return gate.SendAsync(token => BybitMarketDataClient.RequestListAsync(bybit, category, token), cancellationToken);
    }

    public async Task<IReadOnlyList<Candle>> GetMinuteCandlesAsync(Instrument instrument, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        // Bybit's end is inclusive and the page comes newest first.
        IReadOnlyList<Candle> page = await gate.SendAsync(
            token => BybitMarketDataClient.RequestPageAsync(bybit, instrument, Interval.OneMinute, from, to.AddMilliseconds(-1), token),
            cancellationToken);

        return [.. page.Reverse()];
    }

    public Task<IReadOnlyList<MarketTicker>> GetTickersAsync(MarketCategory category, CancellationToken cancellationToken)
    {
        return gate.SendAsync<IReadOnlyList<MarketTicker>>(
            async token => category == MarketCategory.Spot
                ? BybitMarketDataClient.Unwrap(await bybit.V5Api.ExchangeData.GetSpotTickersAsync(ct: token)).List
                    .Select(ticker => new MarketTicker(new Instrument(category, ticker.Symbol), ticker.LastPrice, ticker.BestBidPrice, ticker.BestAskPrice))
                    .ToList()
                : BybitMarketDataClient.Unwrap(await bybit.V5Api.ExchangeData.GetLinearInverseTickersAsync(Category.Linear, ct: token)).List
                    .Select(ticker => new MarketTicker(
                        new Instrument(category, ticker.Symbol),
                        ticker.LastPrice,
                        ticker.BestBidPrice,
                        ticker.BestAskPrice,
                        ticker.OpenInterest,
                        ticker.FundingRate))
                    .ToList(),
            cancellationToken);
    }

    /// <summary>Every page of the period, by the cursor of Bybit; each page is one request through the gate.</summary>
    public async Task<IReadOnlyList<OpenInterestPoint>> GetOpenInterestAsync(string symbol, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var points = new List<OpenInterestPoint>();
        string? cursor = null;

        do
        {
            BybitResponse<BybitOpenInterest> page = await gate.SendAsync(
                async token => BybitMarketDataClient.Unwrap(await bybit.V5Api.ExchangeData.GetOpenInterestAsync(
                    Category.Linear,
                    symbol,
                    OpenInterestInterval.FiveMinutes,
                    from.UtcDateTime,
                    to.AddMilliseconds(-1).UtcDateTime,
                    HistoryPageSize,
                    cursor,
                    token)),
                cancellationToken);
            points.AddRange(page.List.Select(point => new OpenInterestPoint(Utc(point.Timestamp), point.OpenInterest)));
            cursor = page.NextPageCursor;
        }
        while (!string.IsNullOrEmpty(cursor));

        return [.. points.OrderBy(point => point.Time)];
    }

    /// <summary>Bybit pages funding history by time only: each page ends just before the oldest settlement of the one before.</summary>
    public async Task<IReadOnlyList<FundingRatePoint>> GetFundingHistoryAsync(string symbol, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var rates = new List<FundingRatePoint>();
        DateTimeOffset end = to.AddMilliseconds(-1);

        while (true)
        {
            DateTimeOffset pageEnd = end;
            BybitFundingHistory[] page = (await gate.SendAsync(
                async token => BybitMarketDataClient.Unwrap(
                    await bybit.V5Api.ExchangeData.GetFundingRateHistoryAsync(Category.Linear, symbol, from.UtcDateTime, pageEnd.UtcDateTime, HistoryPageSize, token)),
                cancellationToken)).List;
            rates.AddRange(page.Select(rate => new FundingRatePoint(Utc(rate.Timestamp), rate.FundingRate)));

            if (page.Length < HistoryPageSize)
            {
                return [.. rates.OrderBy(rate => rate.Time)];
            }

            end = rates.Min(rate => rate.Time).AddMilliseconds(-1);
        }
    }

    private static DateTimeOffset Utc(DateTime time)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc));
    }
}