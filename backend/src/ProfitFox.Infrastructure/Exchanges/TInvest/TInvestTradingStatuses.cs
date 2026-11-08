using ProfitFox.Application.Exceptions;

using Tinkoff.InvestApi.V1;

using OrderType = ProfitFox.Domain.Entities.Strategies.Enums.OrderType;

namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>
///     What the exchange takes now (decision 6): the trading status of a share and its price corridor, each read at most every
///     <see cref="StatusLifetime" />, and the schedule of an exchange, kept to the end of the day. The next session opens with the first
///     interval of the exchange (the auctions and the sessions of the morning, the main and the evening ones) after now; the hours of the
///     dealer do not count, and a day with an interval of another type tells none. One for the process.
/// </summary>
internal sealed class TInvestTradingStatuses(TInvestCall call, TimeProvider time)
{
    public static readonly TimeSpan StatusLifetime = TimeSpan.FromSeconds(5);

    private const string DealerPrefix = "dealer_";

    private static readonly HashSet<string> ExchangeIntervals =
    [
        "opening_auction_morning",
        "regular_trading_session_morning",
        "regular_trading_session_main",
        "regular_trading_session_evening",
        "closing_auction_main"
    ];

    private readonly Lock _gate = new Lock();
    private readonly Dictionary<string, (DateTimeOffset Expires, GetTradingStatusResponse Status)> _statuses = [];
    private readonly Dictionary<string, (DateTimeOffset Expires, GetOrderBookResponse Book)> _books = [];
    private readonly Dictionary<string, (DateTimeOffset Expires, TradingSchedulesResponse Schedule)> _schedules = [];

    /// <exception cref="ExchangeException">T-Invest refused or did not answer.</exception>
    public async Task<TInvestPlacement> MayPlaceAsync(string token, TInvestShare share, OrderType type, decimal? price, CancellationToken cancellationToken)
    {
        GetTradingStatusResponse status = await CachedAsync(
            _statuses,
            share.Uid,
            StatusLifetime,
            () => call.OnceAsync(token, invoker => new MarketDataService.MarketDataServiceClient(invoker).GetTradingStatusAsync(new GetTradingStatusRequest { InstrumentId = share.Uid }), cancellationToken));

        if (!status.ApiTradeAvailableFlag || !(type == OrderType.Limit ? status.LimitOrderAvailableFlag : status.MarketOrderAvailableFlag))
        {
            return TInvestPlacement.SessionClosed;
        }

        if (type != OrderType.Limit)
        {
            return TInvestPlacement.Open;
        }

        GetOrderBookResponse book = await CachedAsync(
            _books,
            share.Uid,
            StatusLifetime,
            () => call.OnceAsync(token, invoker => new MarketDataService.MarketDataServiceClient(invoker).GetOrderBookAsync(new GetOrderBookRequest { InstrumentId = share.Uid, Depth = 1 }), cancellationToken));
        decimal down = book.LimitDown?.ToDecimal() ?? 0m;
        decimal up = book.LimitUp?.ToDecimal() ?? 0m;

        // An empty or a turned over corridor tells nothing: the order waits for one that makes sense.
        return down > 0m && up >= down && price >= down && price <= up ? TInvestPlacement.Open : TInvestPlacement.OutOfCorridor;
    }

    /// <summary>When the next session of the exchange opens, or <c>null</c> when the schedule does not read or tells none.</summary>
    public async Task<DateTimeOffset?> NextSessionStartAsync(string token, string exchange, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        TradingSchedulesResponse schedule;

        try
        {
            schedule = await CachedAsync(
                _schedules,
                exchange,
                now.UtcDateTime.Date.AddDays(1) - now.UtcDateTime,
                () => call.OnceAsync(
                    token,
                    invoker => new InstrumentsService.InstrumentsServiceClient(invoker).TradingSchedulesAsync(
                        new TradingSchedulesRequest { Exchange = exchange, From = now.ToProtoTimestamp(), To = now.AddDays(7).ToProtoTimestamp() }),
                    cancellationToken));
        }
        catch (ExchangeException)
        {
            return null;
        }

        foreach (TradingDay day in schedule.Exchanges.SelectMany(item => item.Days).OrderBy(day => day.Date.ToUtcMoment()))
        {
            if (day.Intervals.Any(interval => !ExchangeIntervals.Contains(interval.Type) && !interval.Type.StartsWith(DealerPrefix, StringComparison.Ordinal)))
            {
                return null;
            }

            DateTimeOffset[] starts =
            [
                .. day.Intervals
                    .Where(interval => ExchangeIntervals.Contains(interval.Type))
                    .Select(interval => interval.Interval.StartTs.ToUtcMoment())
                    .Where(start => start > now)
                    .Order()
            ];

            if (starts.Length > 0)
            {
                return starts[0];
            }
        }

        return null;
    }

    private async Task<T> CachedAsync<T>(Dictionary<string, (DateTimeOffset Expires, T Value)> cache, string key, TimeSpan lifetime, Func<Task<T>> read)
    {
        DateTimeOffset now = time.GetUtcNow();

        lock (_gate)
        {
            if (cache.TryGetValue(key, out (DateTimeOffset Expires, T Value) cached) && now < cached.Expires)
            {
                return cached.Value;
            }
        }

        T value = await read();

        lock (_gate)
        {
            cache[key] = (now + lifetime, value);
        }

        return value;
    }
}