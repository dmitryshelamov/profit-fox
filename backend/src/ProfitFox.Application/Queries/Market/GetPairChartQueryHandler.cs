using MediatR;

using ProfitFox.Application.Contracts.Preview;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Queries.Market;

/// <summary>
///     Pages of 500 candles back from the REST of the market-data service, the current day included; the open interest is
///     the last point of each candle.
/// </summary>
internal sealed class GetPairChartQueryHandler(IInstrumentCatalog catalog, IRecentHistory history, TimeProvider time) : IRequestHandler<GetPairChartQuery, PairChartInternal?>
{
    public const int Page = 500;

    /// <exception cref="IdeasRequestException">The timeframe is not one of the five.</exception>
    /// <exception cref="HistoryUnavailableException">The service did not answer.</exception>
    public async Task<PairChartInternal?> Handle(GetPairChartQuery request, CancellationToken cancellationToken)
    {
        if (!Timeframes.TryParse(request.Timeframe, out TimeSpan timeframe))
        {
            throw new IdeasRequestException([new PreviewErrorInternal("InvalidTimeframe", $"таймфрейм — один из {Timeframes.Listed}") { Field = "timeframe" }]);
        }

        if ((await catalog.LinearUsdtPerpetuals(cancellationToken)).FirstOrDefault(pair => pair.Symbol == request.Symbol) is not { } pair)
        {
            return null;
        }

        DateTimeOffset before = request.Before ?? time.GetUtcNow();
        RecentRead<HistoryCandle> candles = await history.Candles(pair.Id, timeframe, before, Page, cancellationToken);
        if (candles.Loading)
        {
            return new PairChartInternal(timeframe, Loading: true, [], [], []);
        }

        DateTimeOffset from = candles.Items.Count > 0 ? candles.Items[0].OpenTime : before - (timeframe * Page);
        RecentRead<OpenInterestPoint> interest = await history.OpenInterest(pair.Id, timeframe, before, Page, cancellationToken);
        RecentRead<HistoryPayment> funding = await history.Funding(pair.Id, from, before, cancellationToken);
        return new PairChartInternal(timeframe, interest.Loading || funding.Loading, candles.Items, interest.Items, funding.Items);
    }
}
