using System.Runtime.CompilerServices;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

using Tinkoff.InvestApi.V1;

using Candle = ProfitFox.Domain.Entities.MarketData.Candle;
using Instrument = ProfitFox.Domain.ValueObjects.MarketData.Instrument;

namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>
///     The market data of MOEX through T-Invest, with the token of the stored T-Invest account: the catalog of the shares of TQBR, the
///     rules of a share (its lot is the step and the minimum of the volume, the minimum amount is zero), its last price and its candles.
///     Candles come window by window from the end of the period, the source <c>Exchange</c> and no limit, complete ones only, the
///     volume in shares (lots × lot) and the turnover volume × (high + low + close) / 3; a window without candles is a night or a weekend.
/// </summary>
internal sealed class TInvestMarketDataClient(TInvestCall call, TInvestTokenSource tokens, TInvestShares shares) : IMarketHistoryClient, IInstrumentRulesProvider, IInstrumentCatalog
{
    public async IAsyncEnumerable<IReadOnlyList<Candle>> GetCandlePagesAsync(
        Instrument instrument,
        Interval interval,
        DateTimeOffset from,
        DateTimeOffset to,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        TInvestShare share = await shares.ShareByAsync(instrument.Symbol, cancellationToken);
        string token = await tokens.GetAsync(cancellationToken);

        foreach ((DateTimeOffset start, DateTimeOffset end) in Windows(interval, from, to))
        {
            IReadOnlyList<Candle> page = await RequestAsync(token, share, interval, start, end, retries: true, cancellationToken);

            if (page.Count > 0)
            {
                yield return page;
            }
        }
    }

    // A single attempt per window, like Bybit: a person waits for the page of a chart.
    public async Task<IReadOnlyList<Candle>> GetCandlesOnceAsync(Instrument instrument, Interval interval, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        TInvestShare share = await shares.ShareByAsync(instrument.Symbol, cancellationToken);
        string token = await tokens.GetAsync(cancellationToken);
        var candles = new List<Candle>();

        foreach ((DateTimeOffset start, DateTimeOffset end) in Windows(interval, from, to))
        {
            candles.AddRange(await RequestAsync(token, share, interval, start, end, retries: false, cancellationToken));
        }

        return candles;
    }

    public async Task<decimal> GetLastPriceAsync(Instrument instrument, CancellationToken cancellationToken)
    {
        TInvestShare share = await shares.ShareByAsync(instrument.Symbol, cancellationToken);
        string token = await tokens.GetAsync(cancellationToken);
        GetLastPricesResponse response = await LastPricesAsync(token, [share.Uid], cancellationToken);

        return response.LastPrices.Where(price => price.InstrumentUid == share.Uid).Select(price => price.Price.ToDecimal()).FirstOrDefault() is { } last and > 0m
            ? last
            : throw new ExchangeUnavailableException($"T-Invest has no last price of {instrument.Symbol}.");
    }

    public async Task<IReadOnlyDictionary<string, decimal>> GetLastPricesAsync(MarketCategory category, CancellationToken cancellationToken)
    {
        string token = await tokens.GetAsync(cancellationToken);
        IReadOnlyList<TInvestShare> listed = await SharesAsync(token, cancellationToken);
        var tickers = listed.ToDictionary(share => share.Uid, share => share.Ticker);
        GetLastPricesResponse response = await LastPricesAsync(token, tickers.Keys, cancellationToken);

        return response.LastPrices
            .Where(price => tickers.ContainsKey(price.InstrumentUid))
            .ToDictionary(price => tickers[price.InstrumentUid], price => price.Price.ToDecimal());
    }

    public async Task<bool> InstrumentExistsAsync(Instrument instrument, CancellationToken cancellationToken)
    {
        return await shares.TryShareByAsync(instrument.Symbol, cancellationToken) is not null;
    }

    public async Task<InstrumentRules> GetRulesAsync(Instrument instrument, CancellationToken cancellationToken)
    {
        TInvestShare share = await shares.ShareByAsync(instrument.Symbol, cancellationToken);

        return new InstrumentRules(share.MinPriceIncrement, share.Lot, share.Lot, 0m);
    }

    /// <summary>Every share of TQBR the system trades, in one answer of <c>Shares</c> with the base status.</summary>
    public async Task<IReadOnlyList<ListedInstrument>> ListAsync(MarketCategory category, CancellationToken cancellationToken)
    {
        string token = await tokens.GetAsync(cancellationToken);

        return [.. (await SharesAsync(token, cancellationToken)).Select(share => Listed(category, share))];
    }

    private static ListedInstrument Listed(MarketCategory category, TInvestShare share)
    {
        return new ListedInstrument(
            category,
            share.Ticker,
            share.Ticker,
            "RUB",
            Trading: true,
            TickSize: share.MinPriceIncrement,
            TradFi: true,
            QuantityStep: share.Lot,
            MinQuantity: share.Lot,
            MinAmount: 0m,
            Name: share.Name,
            ShortEnabled: share.ShortEnabled);
    }

    private async Task<IReadOnlyList<TInvestShare>> SharesAsync(string token, CancellationToken cancellationToken)
    {
        var request = new InstrumentsRequest { InstrumentStatus = InstrumentStatus.Base };
        SharesResponse response = await call.OnceAsync(token, invoker => new InstrumentsService.InstrumentsServiceClient(invoker).SharesAsync(request), cancellationToken);

        return [.. response.Instruments.Select(TInvestShares.Tradable).OfType<TInvestShare>()];
    }

    private Task<GetLastPricesResponse> LastPricesAsync(string token, IEnumerable<string> uids, CancellationToken cancellationToken)
    {
        var request = new GetLastPricesRequest { InstrumentId = { uids }, LastPriceType = LastPriceType.LastPriceExchange };

        return call.OnceAsync(token, invoker => new MarketDataService.MarketDataServiceClient(invoker).GetLastPricesAsync(request), cancellationToken);
    }

    /// <summary>The windows of the period from its end back, each at most the window of the interval, so the newest candles come first.</summary>
    internal static IEnumerable<(DateTimeOffset From, DateTimeOffset To)> Windows(Interval interval, DateTimeOffset from, DateTimeOffset to)
    {
        TimeSpan window = interval.Window();

        for (DateTimeOffset end = to; end > from; end -= window)
        {
            DateTimeOffset start = end - window > from ? end - window : from;

            yield return (start, end);
        }
    }

    private async Task<IReadOnlyList<Candle>> RequestAsync(string token, TInvestShare share, Interval interval, DateTimeOffset from, DateTimeOffset to, bool retries, CancellationToken cancellationToken)
    {
        var request = new GetCandlesRequest
        {
            InstrumentId = share.Uid,
            Interval = interval.ToTInvest(),
            From = from.ToProtoTimestamp(),
            To = to.ToProtoTimestamp(),
            CandleSourceType = GetCandlesRequest.Types.CandleSource.Exchange
        };
        GetCandlesResponse response = retries
            ? await call.WithRetriesAsync(token, invoker => new MarketDataService.MarketDataServiceClient(invoker).GetCandlesAsync(request), cancellationToken)
            : await call.OnceAsync(token, invoker => new MarketDataService.MarketDataServiceClient(invoker).GetCandlesAsync(request), cancellationToken);

        return
        [
            .. response.Candles
                .Where(candle => candle.IsComplete)
                .Select(candle => ToCandle(candle, share.Lot))
                .Where(candle => candle.OpenTime >= from && candle.OpenTime < to)
                .OrderByDescending(candle => candle.OpenTime)
        ];
    }

    private static Candle ToCandle(HistoricCandle candle, int lot)
    {
        decimal high = candle.High.ToDecimal();
        decimal low = candle.Low.ToDecimal();
        decimal close = candle.Close.ToDecimal();
        decimal volume = candle.Volume * lot;

        return new Candle(
            candle.Time.ToUtcMoment(),
            new Price(candle.Open.ToDecimal()),
            new Price(high),
            new Price(low),
            new Price(close),
            new Volume(volume),
            new Volume(volume * (high + low + close) / 3m));
    }
}