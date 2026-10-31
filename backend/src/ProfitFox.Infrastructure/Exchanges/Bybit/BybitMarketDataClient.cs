using System.Net;
using System.Runtime.CompilerServices;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Objects.Models.V5;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Errors;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Exchanges.Retry;
using Interval = ProfitFox.Domain.Entities.MarketData.Enums.Interval;
using MarketCategory = ProfitFox.Domain.Entities.MarketData.Enums.MarketCategory;

namespace ProfitFox.Infrastructure.Exchanges.Bybit;

/// <summary>Public Bybit v5 market data through Bybit.Net; no API key, never sends orders.</summary>
internal sealed class BybitMarketDataClient(IBybitRestClient bybit, ExchangeResilienceExecutor resilience) : IMarketHistoryClient
{
    private const int PageSize = 1000;

    public async IAsyncEnumerable<IReadOnlyList<Candle>> GetCandlePagesAsync(
        Instrument instrument,
        Interval interval,
        DateTimeOffset from,
        DateTimeOffset to,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Bybit's end is inclusive and pages come newest first, so walk back from just before `to`.
        var end = to.AddMilliseconds(-1);
        while (true)
        {
            var page = await GetPageAsync(instrument, interval, from, end, cancellationToken);
            if (page.Count == 0)
            {
                yield break;
            }

            yield return page;

            var earliest = page[^1].OpenTime;
            if (earliest <= from)
            {
                yield break;
            }

            end = earliest.AddMilliseconds(-1);
        }
    }

    // A single attempt: the caller waits for this answer synchronously.
    public async Task<bool> InstrumentExistsAsync(Instrument instrument, CancellationToken cancellationToken)
    {
        try
        {
            return await resilience.ExecuteOnceAsync(
                async token => instrument.Category == MarketCategory.Spot
                    ? Unwrap(await bybit.V5Api.ExchangeData.GetSpotSymbolsAsync(instrument.Symbol, token)).List.Length > 0
                    : Unwrap(await bybit.V5Api.ExchangeData.GetLinearInverseSymbolsAsync(
                        instrument.Category.ToBybit(), instrument.Symbol, ct: token)).List.Length > 0,
                cancellationToken);
        }
        catch (TransientExchangeException exception)
        {
            throw new ExchangeException($"Bybit is unavailable: {exception.Message}");
        }
    }

    private async Task<IReadOnlyList<Candle>> GetPageAsync(
        Instrument instrument, Interval interval, DateTimeOffset from, DateTimeOffset end, CancellationToken cancellationToken)
    {
        try
        {
            var klines = await resilience.ExecuteWithRetriesAsync(
                async token => Unwrap(await bybit.V5Api.ExchangeData.GetKlinesAsync(
                    instrument.Category.ToBybit(), instrument.Symbol, interval.ToBybit(), from.UtcDateTime, end.UtcDateTime, PageSize, token)),
                cancellationToken);
            return klines.List.Select(ToCandle).ToList();
        }
        catch (TransientExchangeException exception)
        {
            throw new ExchangeException($"Bybit request failed after {resilience.RetryCount} retries: {exception.Message}");
        }
    }

    /// <summary>The data of a successful call; a failure worth retrying is a <see cref="TransientExchangeException"/>, any other an <see cref="ExchangeException"/>.</summary>
    private static T Unwrap<T>(HttpResult<T> result)
    {
        if (result.Success)
        {
            return result.Data!;
        }

        // Rate limit (HTTP 429 or retCode 10006), timeout and no connection come as error types; a 5xx only by its status.
        if (result.ResponseStatusCode >= HttpStatusCode.InternalServerError
            || result.Error!.ErrorType is ErrorType.RateLimitRequest or ErrorType.Timeout or ErrorType.NetworkError)
        {
            throw new TransientExchangeException(Describe(result));
        }

        throw new ExchangeException(result.Error.Message!);
    }

    // The SDK leaves the message of HTTP 429 empty and writes the others as "<code>: <message>" with an empty code.
    private static string Describe<T>(HttpResult<T> result) =>
        result.ResponseStatusCode is { } status && status != HttpStatusCode.OK
            ? $"HTTP {(int)status}"
            : result.Error!.Message!.TrimStart(':', ' ');

    private static Candle ToCandle(BybitKline kline) => new(
        new DateTimeOffset(DateTime.SpecifyKind(kline.StartTime, DateTimeKind.Utc)),
        new Price(kline.OpenPrice),
        new Price(kline.HighPrice),
        new Price(kline.LowPrice),
        new Price(kline.ClosePrice),
        new Volume(kline.Volume),
        new Volume(kline.QuoteVolume));
}
