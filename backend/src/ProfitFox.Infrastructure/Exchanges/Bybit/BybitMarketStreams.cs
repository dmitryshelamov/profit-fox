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
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;

using MarketCategory = ProfitFox.Domain.Entities.MarketData.Enums.MarketCategory;

namespace ProfitFox.Infrastructure.Exchanges.Bybit;

/// <summary>
///     The minute candles of the whole market on a socket client of their own, apart from <see cref="BybitStreams" /> of the bots
///     (decision 5): <see cref="BatchSize" /> topics in a subscribe request, as many requests on a connection as
///     <see cref="ScannerSettings.TopicsPerConnection" /> allows. CryptoExchange.Net reconnects and subscribes again.
/// </summary>
internal sealed class BybitMarketStreams(ILoggerFactory loggers, BybitSettings settings, ScannerSettings scanner) : IMarketStreams
{
    // The most topics Bybit takes in one subscribe request of spot; linear uses the same so that both lay out alike.
    private const int BatchSize = 10;

    public async Task<IAsyncDisposable> SubscribeMinuteCandlesAsync(
        IReadOnlyList<Instrument> instruments,
        Action<Instrument, Candle> closed,
        Action restored,
        CancellationToken cancellationToken)
    {
        // The client puts this many topics on a connection before it opens another; it counts each topic of a batch.
        var client = new BybitSocketClient(
            Options.Create(new BybitSocketOptions
            {
                Environment = settings.Environment(),
                SocketIndividualSubscriptionCombineTarget = scanner.TopicsPerConnection
            }),
            loggers);
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

    private sealed class Stream(BybitSocketClient client) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await client.UnsubscribeAllAsync();
            client.Dispose();
        }
    }
}