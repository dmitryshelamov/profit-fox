using MediatR;

using Microsoft.Extensions.Logging;

using ProfitFox.Application.Contracts.Preview;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Services.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Queries.Market;

/// <summary>
///     Pages of 500 candles back from the REST of the market-data service, the current day included, with the payments of
///     funding of that time read together with them; the open interest of the page is <see cref="GetPairOpenInterestQuery" />
///     (task 19.7). The journal tells how long the catalog, the candles and the payments took (task 20.9); the pair is found in
///     the list of instruments kept for a minute (task 21.5). The candles and the payments are read under the limit of the
///     page (task 23.2): a read out of time answers that the chart loads, and the page asks again by its own timer.
/// </summary>
internal sealed class GetPairChartQueryHandler(
    ChartPairs pairs,
    IRecentHistory history,
    TimeProvider time,
    ILogger<GetPairChartQueryHandler>? logger = null,
    PageReads? reads = null) : IRequestHandler<GetPairChartQuery, PairChartInternal?>
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

        long started = time.GetTimestamp();
        if (await pairs.Find(request.Symbol, cancellationToken) is not { } pair)
        {
            return null;
        }

        TimeSpan catalogTook = time.GetElapsedTime(started);

        // The candles and the payments go together (task 20.9): the payments over the window of the candles, from its start
        // — the window ends at the boundary of the timeframe before `before`, 500 candles long.
        DateTimeOffset before = request.Before ?? time.GetUtcNow();
        var end = new DateTimeOffset(before.UtcTicks - (before.UtcTicks % timeframe.Ticks), TimeSpan.Zero);
        long reading = time.GetTimestamp();
        IReadSteps steps = (reads ?? new PageReads(time)).Of(request.Symbol);
        Task<(RecentRead<HistoryCandle> Read, TimeSpan Took)> candlesRead = Timed(Within(steps, "candles", token => history.Candles(pair.Id, timeframe, before, Page, token), cancellationToken));
        Task<(RecentRead<HistoryPayment> Read, TimeSpan Took)> fundingRead = Timed(Within(steps, "funding", token => history.Funding(pair.Id, end - (timeframe * Page), before, token), cancellationToken));
        try
        {
            await Task.WhenAll(candlesRead, fundingRead);
        }
        catch (Exception) when (candlesRead.IsCompletedSuccessfully && candlesRead.Result.Read.Loading)
        {
            // The payments of a pair whose candles are still loading do not matter: the page is loading.
        }

        (RecentRead<HistoryCandle> candles, TimeSpan candlesTook) = await candlesRead;
        if (candles.Loading)
        {
            return new PairChartInternal(timeframe, Loading: true, [], []);
        }

        (RecentRead<HistoryPayment> funding, TimeSpan fundingTook) = await fundingRead;
        logger?.LogInformation(
            "The chart of {Symbol} on {Timeframe}: catalog {Catalog} ms, candles {Candles} ms, funding {Funding} ms, read {Read} ms, in all {Total} ms",
            request.Symbol,
            request.Timeframe,
            (long)catalogTook.TotalMilliseconds,
            (long)candlesTook.TotalMilliseconds,
            (long)fundingTook.TotalMilliseconds,
            (long)time.GetElapsedTime(reading).TotalMilliseconds,
            (long)time.GetElapsedTime(started).TotalMilliseconds);

        // A payment before the first candle — a pair listed within the window — would stand on no candle of the chart.
        IReadOnlyList<HistoryPayment> payments = candles.Items.Count > 0 && funding.Items.Count > 0 && funding.Items[0].Time < candles.Items[0].OpenTime
            ? [.. funding.Items.Where(payment => payment.Time >= candles.Items[0].OpenTime)]
            : funding.Items;
        return new PairChartInternal(timeframe, funding.Loading, candles.Items, payments);
    }

    // A read under the limit of the page: out of time, it is still loading (task 23.2).
    private static async Task<RecentRead<T>> Within<T>(IReadSteps steps, string step, Func<CancellationToken, Task<RecentRead<T>>> read, CancellationToken cancellation)
    {
        try
        {
            return await steps.Run(step, read, cancellation);
        }
        catch (ReadTimedOutException)
        {
            return RecentRead<T>.StillLoading;
        }
    }

    // A read and how long it took, counted by the clock of the handler.
    private async Task<(T Read, TimeSpan Took)> Timed<T>(Task<T> read)
    {
        long started = time.GetTimestamp();
        T result = await read;
        return (result, time.GetElapsedTime(started));
    }
}
