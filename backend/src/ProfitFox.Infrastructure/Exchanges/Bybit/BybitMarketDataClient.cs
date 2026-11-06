using System.Net;
using System.Runtime.CompilerServices;

using Bybit.Net.Enums;
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
internal sealed class BybitMarketDataClient(IBybitRestClient bybit, ExchangeResilienceExecutor resilience) : IMarketHistoryClient, IInstrumentRulesProvider, IInstrumentCatalog
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
        DateTimeOffset end = to.AddMilliseconds(-1);

        while (true)
        {
            IReadOnlyList<Candle> page = await GetPageAsync(instrument, interval, from, end, cancellationToken);

            if (page.Count == 0)
            {
                yield break;
            }

            yield return page;

            DateTimeOffset earliest = page[^1].OpenTime;

            if (earliest <= from)
            {
                yield break;
            }

            end = earliest.AddMilliseconds(-1);
        }
    }

    // A single attempt, like the instrument check: a person waits for the page of a chart.
    public async Task<IReadOnlyList<Candle>> GetCandlesOnceAsync(
        Instrument instrument,
        Interval interval,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        try
        {
            return await resilience.ExecuteOnceAsync(
                async token => await RequestPageAsync(instrument, interval, from, to.AddMilliseconds(-1), token),
                cancellationToken);
        }
        catch (TransientExchangeException exception)
        {
            throw exception.Unavailable($"Bybit is unavailable: {exception.Message}");
        }
    }

    public async Task<decimal> GetLastPriceAsync(Instrument instrument, CancellationToken cancellationToken)
    {
        try
        {
            return await resilience.ExecuteOnceAsync(
                async token => instrument.Category == MarketCategory.Spot
                    ? Unwrap(await bybit.V5Api.ExchangeData.GetSpotTickersAsync(instrument.Symbol, token)).List.Where(ticker => ticker.Symbol == instrument.Symbol).Select(ticker => ticker.LastPrice).FirstOrDefault()
                    : Unwrap(await bybit.V5Api.ExchangeData.GetLinearInverseTickersAsync(instrument.Category.ToBybit(), instrument.Symbol, ct: token)).List.Where(ticker => ticker.Symbol == instrument.Symbol).Select(ticker => ticker.LastPrice).FirstOrDefault(),
                cancellationToken) is { } price and > 0m
                ? price
                : throw new ExchangeUnavailableException($"Bybit has no ticker of {instrument.Symbol}.");
        }
        catch (TransientExchangeException exception)
        {
            throw new ExchangeUnavailableException($"Bybit is unavailable: {exception.Message}");
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
                    : Unwrap(await bybit.V5Api.ExchangeData.GetLinearInverseSymbolsAsync(instrument.Category.ToBybit(), instrument.Symbol, ct: token)).List.Length > 0,
                cancellationToken);
        }
        catch (TransientExchangeException exception)
        {
            throw new ExchangeException($"Bybit is unavailable: {exception.Message}");
        }
    }

    /// <summary>
    ///     A single attempt, like the instrument check: spot gives the base precision and the minimum order value, linear the quantity
    ///     step and the minimum notional value.
    /// </summary>
    public async Task<InstrumentRules> GetRulesAsync(Instrument instrument, CancellationToken cancellationToken)
    {
        try
        {
            return await resilience.ExecuteOnceAsync(
                async token =>
                {
                    if (instrument.Category == MarketCategory.Spot)
                    {
                        BybitSpotSymbol spot = Listed(Unwrap(await bybit.V5Api.ExchangeData.GetSpotSymbolsAsync(instrument.Symbol, token)).List, instrument);

                        return new InstrumentRules(
                            spot.PriceFilter!.TickSize,
                            spot.LotSizeFilter!.BasePrecision,
                            spot.LotSizeFilter.MinOrderQuantity,
                            spot.LotSizeFilter.MinOrderValue);
                    }

                    BybitLinearInverseSymbol linear = Listed(
                        Unwrap(await bybit.V5Api.ExchangeData.GetLinearInverseSymbolsAsync(instrument.Category.ToBybit(), instrument.Symbol, ct: token)).List,
                        instrument);

                    return new InstrumentRules(
                        linear.PriceFilter!.TickSize,
                        linear.LotSizeFilter!.QuantityStep,
                        linear.LotSizeFilter.MinOrderQuantity,
                        linear.LotSizeFilter.MinNotionalValue ?? 0m);
                },
                cancellationToken);
        }
        catch (TransientExchangeException exception)
        {
            throw new ExchangeUnavailableException($"Bybit is unavailable: {exception.Message}");
        }
    }

    /// <summary>Every instrument of the category, one attempt per request: a person waits for the answer of a search.</summary>
    public async Task<IReadOnlyList<ListedInstrument>> ListAsync(MarketCategory category, CancellationToken cancellationToken)
    {
        try
        {
            return await resilience.ExecuteOnceAsync(async token => await RequestListAsync(bybit, category, token), cancellationToken);
        }
        catch (TransientExchangeException exception)
        {
            throw new ExchangeUnavailableException($"Bybit is unavailable: {exception.Message}");
        }
    }

    /// <summary>Every instrument of the category in one attempt per request: spot in one answer, linear by following the cursor.</summary>
    internal static async ValueTask<IReadOnlyList<ListedInstrument>> RequestListAsync(IBybitRestClient bybit, MarketCategory category, CancellationToken cancellationToken)
    {
        if (category == MarketCategory.Spot)
        {
            // Bybit lists all spot instruments in one answer.
            return Unwrap(await bybit.V5Api.ExchangeData.GetSpotSymbolsAsync(ct: cancellationToken)).List
                .Select(spot => new ListedInstrument(
                    category,
                    spot.Name,
                    spot.BaseAsset,
                    spot.QuoteAsset,
                    spot.Status == SymbolStatus.Trading,
                    TickSize: spot.PriceFilter?.TickSize,
                    TradFi: IsTradFi(spot.SymbolType),
                    QuantityStep: spot.LotSizeFilter?.BasePrecision,
                    MinQuantity: spot.LotSizeFilter?.MinOrderQuantity,
                    MinAmount: spot.LotSizeFilter?.MinOrderValue))
                .ToList();
        }

        var listed = new List<ListedInstrument>();
        string? cursor = null;

        do
        {
            BybitResponse<BybitLinearInverseSymbol> page = Unwrap(
                await bybit.V5Api.ExchangeData.GetLinearInverseSymbolsAsync(category.ToBybit(), limit: PageSize, cursor: cursor, ct: cancellationToken));
            listed.AddRange(page.List.Select(linear => new ListedInstrument(
                category,
                linear.Name,
                linear.BaseAsset,
                linear.QuoteAsset,
                linear.Status == SymbolStatus.Trading,
                linear.ContractType == ContractTypeV5.LinearPerpetual,
                new DateTimeOffset(DateTime.SpecifyKind(linear.LaunchTime, DateTimeKind.Utc)),
                linear.PriceFilter?.TickSize,
                IsTradFi(linear.SymbolType),
                linear.LotSizeFilter?.QuantityStep,
                linear.LotSizeFilter?.MinOrderQuantity,
                linear.LotSizeFilter?.MinNotionalValue)));
            cursor = page.NextPageCursor;
        }
        while (!string.IsNullOrEmpty(cursor));

        return listed;
    }

    // A stock (a spot xStock too), an ETF, a commodity or forex; a zone of coins, no type or a type the SDK does not know is crypto.
    private static bool IsTradFi(SymbolType? type)
    {
        return type is SymbolType.Stock or SymbolType.XStocks or SymbolType.Etf or SymbolType.Commodity or SymbolType.Forex;
    }

    private static T Listed<T>(T[] symbols, Instrument instrument)
    {
        return symbols.Length > 0 ? symbols[0] : throw new ExchangeException($"Bybit does not list {instrument.Symbol} in {instrument.Category}.");
    }

    private async Task<IReadOnlyList<Candle>> GetPageAsync(
        Instrument instrument,
        Interval interval,
        DateTimeOffset from,
        DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        try
        {
            return await resilience.ExecuteWithRetriesAsync(
                async token => await RequestPageAsync(instrument, interval, from, end, token),
                cancellationToken);
        }
        catch (TransientExchangeException exception)
        {
            throw new ExchangeUnavailableException($"Bybit request failed after {resilience.RetryCount} retries: {exception.Message}");
        }
    }

    private ValueTask<IReadOnlyList<Candle>> RequestPageAsync(
        Instrument instrument,
        Interval interval,
        DateTimeOffset from,
        DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        return RequestPageAsync(bybit, instrument, interval, from, end, cancellationToken);
    }

    /// <summary>One page of at most 1000 candles with open time in the inclusive <c>[from, end]</c>, newest first, in one attempt.</summary>
    internal static async ValueTask<IReadOnlyList<Candle>> RequestPageAsync(
        IBybitRestClient bybit,
        Instrument instrument,
        Interval interval,
        DateTimeOffset from,
        DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        BybitResponse<BybitKline> klines = Unwrap(
            await bybit.V5Api.ExchangeData.GetKlinesAsync(
                instrument.Category.ToBybit(),
                instrument.Symbol,
                interval.ToBybit(),
                from.UtcDateTime,
                end.UtcDateTime,
                PageSize,
                cancellationToken));

        return klines.List.Select(ToCandle).ToList();
    }

    /// <summary>The data of a successful call; a failure worth retrying is a <see cref="TransientExchangeException" />, any other an <see cref="ExchangeException" />.</summary>
    internal static T Unwrap<T>(HttpResult<T> result)
    {
        if (result.Success)
        {
            return result.Data!;
        }

        // Rate limit (HTTP 429 or retCode 10006), timeout and no connection come as error types; a 5xx and a ban of the IP
        // for the rate limit (HTTP 403) only by their status.
        if (result.ResponseStatusCode is HttpStatusCode.Forbidden || result.Error!.ErrorType is ErrorType.RateLimitRequest)
        {
            throw new TransientExchangeException(Describe(result), rateLimited: true, ipBanned: result.ResponseStatusCode is HttpStatusCode.Forbidden);
        }

        if (result.ResponseStatusCode is >= HttpStatusCode.InternalServerError || result.Error.ErrorType is ErrorType.Timeout or ErrorType.NetworkError)
        {
            throw new TransientExchangeException(Describe(result));
        }

        throw new ExchangeException(result.Error.Message!);
    }

    // The SDK leaves the message of HTTP 429 empty and writes the others as "<code>: <message>" with an empty code.
    private static string Describe<T>(HttpResult<T> result)
    {
        return result.ResponseStatusCode is { } status && status != HttpStatusCode.OK
            ? $"HTTP {(int)status}"
            : result.Error!.Message!.TrimStart(':', ' ');
    }

    private static Candle ToCandle(BybitKline kline)
    {
        return new Candle(
            new DateTimeOffset(DateTime.SpecifyKind(kline.StartTime, DateTimeKind.Utc)),
            new Price(kline.OpenPrice),
            new Price(kline.HighPrice),
            new Price(kline.LowPrice),
            new Price(kline.ClosePrice),
            new Volume(kline.Volume),
            new Volume(kline.QuoteVolume));
    }
}