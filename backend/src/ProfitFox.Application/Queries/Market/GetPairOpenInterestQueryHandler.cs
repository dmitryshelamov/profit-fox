using MediatR;

using ProfitFox.Application.Contracts.Preview;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Queries.Market;

/// <summary>
///     The open interest of the window of a page of the chart: <see cref="GetPairChartQueryHandler.Page" /> intervals before
///     the end of the page, one point a candle.
/// </summary>
internal sealed class GetPairOpenInterestQueryHandler(IInstrumentCatalog catalog, IRecentHistory history, TimeProvider time) : IRequestHandler<GetPairOpenInterestQuery, PairOpenInterestInternal?>
{
    /// <exception cref="IdeasRequestException">The timeframe is not one of the five.</exception>
    /// <exception cref="HistoryUnavailableException">The service did not answer.</exception>
    public async Task<PairOpenInterestInternal?> Handle(GetPairOpenInterestQuery request, CancellationToken cancellationToken)
    {
        if (!Timeframes.TryParse(request.Timeframe, out TimeSpan timeframe))
        {
            throw new IdeasRequestException([new PreviewErrorInternal("InvalidTimeframe", $"таймфрейм — один из {Timeframes.Listed}") { Field = "timeframe" }]);
        }

        if ((await catalog.LinearUsdtPerpetuals(cancellationToken)).FirstOrDefault(pair => pair.Symbol == request.Symbol) is not { } pair)
        {
            return null;
        }

        RecentRead<OpenInterestPoint> interest = await history.OpenInterest(pair.Id, timeframe, request.Before ?? time.GetUtcNow(), GetPairChartQueryHandler.Page, cancellationToken);
        return new PairOpenInterestInternal(timeframe, interest.Loading, interest.Items);
    }
}
