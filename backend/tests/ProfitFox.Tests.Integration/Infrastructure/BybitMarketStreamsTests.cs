using System.Collections.Concurrent;
using System.Globalization;

using Microsoft.Extensions.Logging.Abstractions;

using ProfitFox.Application.Scanner;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Exchanges.Bybit;

using Shouldly;

namespace ProfitFox.Tests.Integration.Infrastructure;

/// <summary>Decision 5: the minute candles of the whole market in batches, against the streams of Bybit v5 on a local port.</summary>
public class BybitMarketStreamsTests
{
    private static readonly DateTimeOffset Open = DateTimeOffset.Parse("2024-03-10T12:04:00Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Sol = new Instrument(MarketCategory.Linear, "SOLUSDT");

    private FakeBybitSocketServer _server = null!;
    private BybitMarketStreams _streams = null!;

    [SetUp]
    public async Task SetUp()
    {
        _server = await FakeBybitSocketServer.StartAsync();
        _streams = new BybitMarketStreams(NullLoggerFactory.Instance, new BybitSettings(SocketAddress: _server.Address), new ScannerSettings { TopicsPerConnection = 100 });
    }

    [TearDown]
    public async Task TearDown()
    {
        await _server.DisposeAsync();
    }

    // Bybit takes at most 10 topics in a subscribe request of spot and at most 100 topics on a connection.
    [Test]
    public async Task Symbols_go_in_batches_within_the_limits_of_a_request_and_of_a_connection()
    {
        Instrument[] linear = [.. Enumerable.Range(0, 250).Select(i => new Instrument(MarketCategory.Linear, $"C{i:000}USDT"))];
        Instrument[] spot = [.. Enumerable.Range(0, 30).Select(i => new Instrument(MarketCategory.Spot, $"S{i:000}USDT"))];

        await using IAsyncDisposable subscription = await _streams.SubscribeMinuteCandlesAsync([.. linear, .. spot], (_, _) => { }, () => { }, CancellationToken.None);

        linear.ShouldAllBe(instrument => _server.Subscribed($"kline.1.{instrument.Symbol}"));
        spot.ShouldAllBe(instrument => _server.Subscribed($"kline.1.{instrument.Symbol}"));
        _server.SubscribeSizes.ShouldAllBe(size => size <= 10);
        _server.TopicsPerConnection("/v5/public/linear").ShouldAllBe(topics => topics <= 100);
        _server.Connections("/v5/public/linear").ShouldBe(3);
        _server.Connections("/v5/public/spot").ShouldBe(1);
    }

    // Requirement "Живые минутные свечи": a candle that is still forming is never stored.
    [Test]
    public async Task Only_closed_candles_come_with_their_instrument()
    {
        var candles = new BlockingCollection<(Instrument, Candle)>();
        await using IAsyncDisposable subscription = await _streams.SubscribeMinuteCandlesAsync([Sol], (instrument, candle) => candles.Add((instrument, candle)), () => { }, CancellationToken.None);

        await _server.PushAsync("kline.1.SOLUSDT", [FakeBybitSocketServer.Kline(Open, TimeSpan.FromMinutes(1), 150m, confirm: false)]);
        await _server.PushAsync("kline.1.SOLUSDT", [FakeBybitSocketServer.Kline(Open, TimeSpan.FromMinutes(1), 151.5m, confirm: true, volume: 2m)]);

        candles.TryTake(out (Instrument Instrument, Candle Candle) closed, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        closed.Instrument.ShouldBe(Sol);
        closed.Candle.ShouldBe(new Candle(Open, new Price(151.5m), new Price(151.5m), new Price(151.5m), new Price(151.5m), new Volume(2m), new Volume(151.5m)));
        candles.TryTake(out _, TimeSpan.FromMilliseconds(200)).ShouldBeFalse();
    }

    // Requirement "Пропуск после перерыва": the scanner loads what the break missed.
    [Test]
    public async Task Connection_back_after_a_break_is_told()
    {
        var restored = new SemaphoreSlim(0);
        await using IAsyncDisposable subscription = await _streams.SubscribeMinuteCandlesAsync([Sol], (_, _) => { }, () => restored.Release(), CancellationToken.None);

        await _server.DropAsync();

        (await restored.WaitAsync(TimeSpan.FromSeconds(30))).ShouldBeTrue();
    }

    [Test]
    public async Task Refused_subscription_means_the_exchange_is_unavailable()
    {
        _server.RefusedTopics["kline.1.SOLUSDT"] = true;

        await Should.ThrowAsync<ProfitFox.Application.Exceptions.ExchangeUnavailableException>(() =>
            _streams.SubscribeMinuteCandlesAsync([Sol], (_, _) => { }, () => { }, CancellationToken.None));
    }

    [Test]
    public async Task Disposing_takes_every_subscription_off()
    {
        IAsyncDisposable subscription = await _streams.SubscribeMinuteCandlesAsync([Sol, new Instrument(MarketCategory.Spot, "BTCUSDT")], (_, _) => { }, () => { }, CancellationToken.None);

        await subscription.DisposeAsync();

        DateTime until = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while ((_server.Connections("/v5/public/linear") + _server.Connections("/v5/public/spot")) > 0 && DateTime.UtcNow < until)
        {
            await Task.Delay(20);
        }

        _server.Subscribed("kline.1.SOLUSDT").ShouldBeFalse();
        _server.Connections("/v5/public/linear").ShouldBe(0);
        _server.Connections("/v5/public/spot").ShouldBe(0);
    }
}