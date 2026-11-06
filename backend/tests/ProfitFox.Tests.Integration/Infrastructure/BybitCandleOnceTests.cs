using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Integration.Infrastructure;

/// <summary>Requirement "Догрузка при чтении истории графика": one Bybit page, one attempt, through the replaced HTTP of the app.</summary>
public class BybitCandleOnceTests
{
    private static readonly Instrument Btc = new(MarketCategory.Spot, "BTCUSDT");
    private ProfitFoxFactory _factory = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = await ProfitFoxFactory.CreateAsync();
        _factory.CreateClient().Dispose();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _factory.DisposeAsync();
    }

    [Test]
    public async Task One_page_of_the_period_is_parsed_exactly()
    {
        _factory.Bybit.Klines.Add(["2024-01-01T00:00:00Z", "100.5", "101.25", "99.75", "100.125", "12.5", "1250.75"]);
        _factory.Bybit.Klines.Add(["2024-01-01T01:00:00Z", "100.125", "102", "100", "101", "7", "700"]);
        _factory.Bybit.Klines.Add(["2024-01-01T02:00:00Z", "1", "1", "1", "1", "1", "1"]);

        IReadOnlyList<Candle> candles = await Client().GetCandlesOnceAsync(
            Btc,
            Interval.OneHour,
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2024-01-01T02:00:00Z"),
            CancellationToken.None);

        candles.Select(candle => candle.OpenTime).ShouldBe([DateTimeOffset.Parse("2024-01-01T01:00:00Z"), DateTimeOffset.Parse("2024-01-01T00:00:00Z")]);
        candles[1].ShouldBe(new Candle(
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            new Price(100.5m),
            new Price(101.25m),
            new Price(99.75m),
            new Price(100.125m),
            new Volume(12.5m),
            new Volume(1250.75m)));
        _factory.Bybit.KlineRequests.Count.ShouldBe(1);
    }

    [Test]
    public async Task A_failing_bybit_gets_exactly_one_request_and_means_the_exchange_is_unavailable()
    {
        _factory.Bybit.Unavailable = true;
        int before = _factory.Bybit.Requests;

        await Should.ThrowAsync<ExchangeUnavailableException>(() => Client().GetCandlesOnceAsync(
            Btc,
            Interval.OneHour,
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2024-01-01T02:00:00Z"),
            CancellationToken.None));
        (_factory.Bybit.Requests - before).ShouldBe(1);
    }

    // Requirement "Расчёт входа": the price of a market idea is the last price of the ticker of its pair, read with one attempt.
    [TestCase(MarketCategory.Spot)]
    [TestCase(MarketCategory.Linear)]
    public async Task Last_price_is_read_from_the_ticker_of_the_pair(MarketCategory category)
    {
        _factory.Bybit.Tickers[(category == MarketCategory.Spot ? "spot" : "linear", "ETHUSDT")] = ("2500.5", "2500.4", "2500.6", "1", "0.0001");
        _factory.Bybit.Tickers[(category == MarketCategory.Spot ? "spot" : "linear", "BTCUSDT")] = ("43000.25", "43000", "43000.5", "1", "0.0001");

        decimal price = await Client().GetLastPriceAsync(new Instrument(category, "BTCUSDT"), CancellationToken.None);

        price.ShouldBe(43000.25m);
        _factory.Bybit.TickerRequests.ShouldBe([category == MarketCategory.Spot ? "spot" : "linear"]);
    }

    [Test]
    public async Task Pair_without_a_ticker_is_unavailable()
    {
        await Should.ThrowAsync<ExchangeUnavailableException>(() => Client().GetLastPriceAsync(Btc, CancellationToken.None));
    }

    private IMarketHistoryClient Client()
    {
        return _factory.Services.GetRequiredService<IMarketHistoryClient>();
    }
}