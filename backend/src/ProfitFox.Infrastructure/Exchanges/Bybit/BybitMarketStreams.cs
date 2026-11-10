using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.V5;
using Bybit.Net.Objects.Options;

using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Sockets;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.Market;
using ProfitFox.Application.Scanner;
using ProfitFox.Domain.Entities.Market.Enums;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

using MarketCategory = ProfitFox.Domain.Entities.MarketData.Enums.MarketCategory;

namespace ProfitFox.Infrastructure.Exchanges.Bybit;

/// <summary>
///     The minute candles of the whole market on a socket client of their own, apart from <see cref="BybitStreams" /> of the bots
///     (decision 5): <see cref="BatchSize" /> topics in a subscribe request, as many requests on a connection as
///     <see cref="ScannerSettings.TopicsPerConnection" /> allows. CryptoExchange.Net reconnects and subscribes again. The liquidations of
///     linear pairs (topic <c>allLiquidation</c>) go the same way on a client of their own, so a break of one stream does not touch the other,
///     and so do the trades of linear and spot pairs (topic <c>publicTrade</c>, decision 4 of collect-trade-flow).
/// </summary>
internal sealed class BybitMarketStreams(ILoggerFactory loggers, BybitSettings settings, ScannerSettings scanner, TimeProvider time) : IMarketStreams
{
    // The most topics Bybit takes in one subscribe request of spot; linear uses the same so that both lay out alike.
    private const int BatchSize = 10;

    public async Task<IAsyncDisposable> SubscribeMinuteCandlesAsync(
        IReadOnlyList<Instrument> instruments,
        Action<Instrument, Candle> closed,
        Action restored,
        CancellationToken cancellationToken)
    {
        BybitSocketClient client = Client();
        var stream = new Stream(client);

        try
        {
            foreach (IGrouping<MarketCategory, Instrument> category in instruments.GroupBy(instrument => instrument.Category))
            {
                foreach (Instrument[] batch in category.Chunk(BatchSize))
                {
                    string[] symbols = [.. batch.Select(instrument => instrument.Symbol)];

                    // Made with no token: CryptoExchange.Net closes a subscription when the token it was made with is cancelled.
                    WebSocketResult<UpdateSubscription> result = category.Key == MarketCategory.Spot
                        ? await client.V5SpotApi.SubscribeToKlineUpdatesAsync(symbols, KlineInterval.OneMinute, update => Klines(category.Key, update, closed), CancellationToken.None)
                        : await client.V5LinearApi.SubscribeToKlineUpdatesAsync(symbols, KlineInterval.OneMinute, update => Klines(category.Key, update, closed), CancellationToken.None);

                    if (!result.Success)
                    {
                        throw new ExchangeUnavailableException($"Bybit refused the stream: {result.Error!.Message}");
                    }

                    result.Data.ConnectionRestored += _ => restored();
                }
            }
        }
        catch
        {
            await stream.DisposeAsync();

            throw;
        }

        return stream;
    }

    public async Task<IAsyncDisposable> SubscribeLiquidationsAsync(
        IReadOnlyList<Instrument> instruments,
        Action<Instrument, LiquidationEvent> liquidated,
        Action<DateTimeOffset> lost,
        Action<DateTimeOffset> restored,
        CancellationToken cancellationToken)
    {
        BybitSocketClient client = Client();
        var stream = new Stream(client);
        var breaks = new Breaks(lost, restored, time);

        try
        {
            foreach (Instrument[] batch in instruments.Chunk(BatchSize))
            {
                // Made with no token: CryptoExchange.Net closes a subscription when the token it was made with is cancelled.
                WebSocketResult<UpdateSubscription> result = await client.V5LinearApi.SubscribeToAllLiquidationUpdatesAsync(
                    [.. batch.Select(instrument => instrument.Symbol)],
                    update => Liquidations(update.Data, liquidated),
                    CancellationToken.None);

                if (!result.Success)
                {
                    throw new ExchangeUnavailableException($"Bybit refused the stream: {result.Error!.Message}");
                }

                result.Data.ConnectionLost += breaks.Lost;
                result.Data.ConnectionRestored += _ => breaks.Restored();
            }
        }
        catch
        {
            await stream.DisposeAsync();

            throw;
        }

        return stream;
    }

    public async Task<IAsyncDisposable> SubscribeTradesAsync(
        IReadOnlyList<Instrument> instruments,
        Action<Instrument, TradeEvent> traded,
        Action<DateTimeOffset> lost,
        Action<DateTimeOffset> restored,
        CancellationToken cancellationToken)
    {
        BybitSocketClient client = Client();
        var stream = new Stream(client);
        var breaks = new Breaks(lost, restored, time);

        try
        {
            foreach (IGrouping<MarketCategory, Instrument> category in instruments.GroupBy(instrument => instrument.Category))
            {
                foreach (Instrument[] batch in category.Chunk(BatchSize))
                {
                    string[] symbols = [.. batch.Select(instrument => instrument.Symbol)];

                    // Made with no token: CryptoExchange.Net closes a subscription when the token it was made with is cancelled.
                    WebSocketResult<UpdateSubscription> result = category.Key == MarketCategory.Spot
                        ? await client.V5SpotApi.SubscribeToTradeUpdatesAsync(symbols, update => Trades(category.Key, update.Data, traded), CancellationToken.None)
                        : await client.V5LinearApi.SubscribeToTradeUpdatesAsync(symbols, update => Trades(category.Key, update.Data, traded), CancellationToken.None);

                    if (!result.Success)
                    {
                        throw new ExchangeUnavailableException($"Bybit refused the stream: {result.Error!.Message}");
                    }

                    result.Data.ConnectionLost += breaks.Lost;
                    result.Data.ConnectionRestored += _ => breaks.Restored();
                }
            }
        }
        catch
        {
            await stream.DisposeAsync();

            throw;
        }

        return stream;
    }

    /// <summary>The client puts this many topics on a connection before it opens another; it counts each topic of a batch.</summary>
    private BybitSocketClient Client()
    {
        return new BybitSocketClient(
            Options.Create(new BybitSocketOptions
            {
                Environment = settings.Environment(),
                SocketIndividualSubscriptionCombineTarget = scanner.TopicsPerConnection
            }),
            loggers);
    }

    // Bybit gives the side of the liquidated position: "when you receive a Buy update, this means that a long position has been liquidated".
    private static void Liquidations(BybitLiquidationUpdate[] updates, Action<Instrument, LiquidationEvent> liquidated)
    {
        foreach (BybitLiquidationUpdate update in updates)
        {
            liquidated(
                new Instrument(MarketCategory.Linear, update.Symbol),
                new LiquidationEvent(
                    new DateTimeOffset(DateTime.SpecifyKind(update.UpdateTime, DateTimeKind.Utc)),
                    update.Side == OrderSide.Buy ? LiquidationSide.Long : LiquidationSide.Short,
                    update.Price,
                    update.Quantity));
        }
    }

    // Every trade counts, a block trade and a trade of the RPI orders too, as in the daily archive (decision 5 of collect-trade-flow).
    private static void Trades(MarketCategory category, BybitTrade[] trades, Action<Instrument, TradeEvent> traded)
    {
        foreach (BybitTrade trade in trades)
        {
            traded(
                new Instrument(category, trade.Symbol),
                new TradeEvent(
                    new DateTimeOffset(DateTime.SpecifyKind(trade.Timestamp, DateTimeKind.Utc)),
                    trade.Side == OrderSide.Buy ? TradeSide.Buy : TradeSide.Sell,
                    trade.Quantity,
                    trade.Price));
        }
    }

    private static void Klines(MarketCategory category, DataEvent<BybitKlineUpdate[]> update, Action<Instrument, Candle> closed)
    {
        foreach (BybitKlineUpdate kline in update.Data.Where(kline => kline.Confirm))
        {
            closed(
                new Instrument(category, update.Symbol!),
                new Candle(
                    new DateTimeOffset(DateTime.SpecifyKind(kline.StartTime, DateTimeKind.Utc)),
                    new Price(kline.OpenPrice),
                    new Price(kline.HighPrice),
                    new Price(kline.LowPrice),
                    new Price(kline.ClosePrice),
                    new Volume(kline.Volume),
                    new Volume(kline.Turnover)));
        }
    }

    /// <summary>One break of the stream for the breaks of its subscriptions: lost with the first lost, back when the last is back.</summary>
    private sealed class Breaks(Action<DateTimeOffset> lost, Action<DateTimeOffset> restored, TimeProvider time)
    {
        private int _down;

        public void Lost()
        {
            if (Interlocked.Increment(ref _down) == 1)
            {
                lost(time.GetUtcNow());
            }
        }

        public void Restored()
        {
            if (Interlocked.Decrement(ref _down) == 0)
            {
                restored(time.GetUtcNow());
            }
        }
    }

    private sealed class Stream(BybitSocketClient client) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await client.UnsubscribeAllAsync();
            client.Dispose();
        }
    }
}