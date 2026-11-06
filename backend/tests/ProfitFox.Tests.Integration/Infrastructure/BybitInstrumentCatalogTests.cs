using Bybit.Net.Interfaces.Clients;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Exchanges.Bybit;
using ProfitFox.Infrastructure.Exchanges.Retry;

using Shouldly;

namespace ProfitFox.Tests.Integration.Infrastructure;

/// <summary>Requirement "Поиск инструментов": the list comes from Bybit instruments-info without a symbol, through the replaced HTTP of the app.</summary>
public class BybitInstrumentCatalogTests
{
    private ProfitFoxFactory _factory = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = await ProfitFoxFactory.CreateAsync(workers: 0);
        _factory.Bybit.Instruments.UnionWith(
            [("spot", "ETHUSDT"), ("spot", "BTCUSDT"), ("linear", "SOLUSDT"), ("linear", "BTCUSDT"), ("linear", "ETHUSDT"), ("linear", "XRPUSDT"), ("linear", "ADAUSDT")]);
        _factory.Bybit.Coins[("spot", "ETHUSDT")] = ("ETH", "USDT");
        _factory.Bybit.Coins[("spot", "BTCUSDT")] = ("BTC", "USDT");
        _factory.Bybit.Coins[("linear", "SOLUSDT")] = ("SOL", "USDT");
        _factory.Bybit.Coins[("linear", "BTCUSDT")] = ("BTC", "USDT");
        _factory.Bybit.Coins[("linear", "ETHUSDT")] = ("ETH", "USDT");
        _factory.Bybit.Coins[("linear", "XRPUSDT")] = ("XRP", "USDT");
        _factory.Bybit.Coins[("linear", "ADAUSDT")] = ("ADA", "USDT");
        _factory.CreateClient().Dispose();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _factory.DisposeAsync();
    }

    [Test]
    public async Task Spot_list_has_every_listed_instrument_with_its_coins()
    {
        IReadOnlyList<ListedInstrument> listed = await Catalog().ListAsync(MarketCategory.Spot, CancellationToken.None);

        listed.ShouldBe(
        [
            new ListedInstrument(MarketCategory.Spot, "BTCUSDT", "BTC", "USDT", Trading: true),
            new ListedInstrument(MarketCategory.Spot, "ETHUSDT", "ETH", "USDT", Trading: true)
        ]);
    }

    // Fix after review: the list tells the step of each price, which the ideas of the scanner go on.
    [Test]
    public async Task List_tells_the_price_step_of_each_instrument()
    {
        _factory.Bybit.Rules[("spot", "BTCUSDT")] = ("0.01", "0.000001", "0.000048", "1");
        _factory.Bybit.Rules[("linear", "SOLUSDT")] = ("0.001", "0.1", "0.1", "5");

        IReadOnlyList<ListedInstrument> spot = await Catalog().ListAsync(MarketCategory.Spot, CancellationToken.None);
        IReadOnlyList<ListedInstrument> linear = await Catalog().ListAsync(MarketCategory.Linear, CancellationToken.None);

        spot.Single(instrument => instrument.Symbol == "BTCUSDT").TickSize.ShouldBe(0.01m);
        spot.Single(instrument => instrument.Symbol == "ETHUSDT").TickSize.ShouldBeNull();
        linear.Single(instrument => instrument.Symbol == "SOLUSDT").TickSize.ShouldBe(0.001m);
    }

    // Task 2.5: the same list tells the step of the volume and the smallest volume and amount of each instrument, spot and linear alike.
    [Test]
    public async Task List_tells_the_step_and_the_minimums_of_the_volume_of_each_instrument()
    {
        _factory.Bybit.Rules[("spot", "BTCUSDT")] = ("0.01", "0.000001", "0.000048", "1");
        _factory.Bybit.Rules[("linear", "SOLUSDT")] = ("0.001", "0.1", "0.2", "5");

        IReadOnlyList<ListedInstrument> spot = await Catalog().ListAsync(MarketCategory.Spot, CancellationToken.None);
        IReadOnlyList<ListedInstrument> linear = await Catalog().ListAsync(MarketCategory.Linear, CancellationToken.None);

        ListedInstrument btc = spot.Single(instrument => instrument.Symbol == "BTCUSDT");
        ListedInstrument sol = linear.Single(instrument => instrument.Symbol == "SOLUSDT");
        (btc.QuantityStep, btc.MinQuantity, btc.MinAmount).ShouldBe((0.000001m, 0.000048m, 1m));
        (sol.QuantityStep, sol.MinQuantity, sol.MinAmount).ShouldBe((0.1m, 0.2m, 5m));
        (spot.Single(instrument => instrument.Symbol == "ETHUSDT").QuantityStep).ShouldBeNull();
    }

    [Test]
    public async Task Linear_list_follows_the_cursor_and_collects_every_page()
    {
        _factory.Bybit.LinearPageSize = 2;

        IReadOnlyList<ListedInstrument> listed = await Catalog().ListAsync(MarketCategory.Linear, CancellationToken.None);

        listed.Select(instrument => instrument.Symbol).ShouldBe(["ADAUSDT", "BTCUSDT", "ETHUSDT", "SOLUSDT", "XRPUSDT"]);
        listed.ShouldAllBe(instrument => instrument.Category == MarketCategory.Linear);
        listed.Single(instrument => instrument.Symbol == "SOLUSDT").ShouldBe(new ListedInstrument(
            MarketCategory.Linear,
            "SOLUSDT",
            "SOL",
            "USDT",
            Trading: true,
            Perpetual: true,
            ListedAt: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        _factory.Bybit.ListRequests.Count.ShouldBe(3);
    }

    // Decision 3: the scanner keeps only instruments that trade.
    [TestCase("spot")]
    [TestCase("linear")]
    public async Task Instrument_out_of_trading_is_listed_as_not_trading(string category)
    {
        _factory.Bybit.Statuses[(category, "ETHUSDT")] = category == "spot" ? "Closed" : "PreLaunch";

        IReadOnlyList<ListedInstrument> listed = await Catalog().ListAsync(category == "spot" ? MarketCategory.Spot : MarketCategory.Linear, CancellationToken.None);

        listed.Single(instrument => instrument.Symbol == "ETHUSDT").Trading.ShouldBeFalse();
        listed.Single(instrument => instrument.Symbol == "BTCUSDT").Trading.ShouldBeTrue();
    }

    // Decision 3: a dated linear future is no perpetual, and a spot pair is never one.
    [Test]
    public async Task Only_a_linear_perpetual_is_listed_as_perpetual()
    {
        _factory.Bybit.Instruments.Add(("linear", "BTCUSDT-27DEC24"));
        _factory.Bybit.ContractTypes["BTCUSDT-27DEC24"] = "LinearFutures";

        IReadOnlyList<ListedInstrument> linear = await Catalog().ListAsync(MarketCategory.Linear, CancellationToken.None);
        IReadOnlyList<ListedInstrument> spot = await Catalog().ListAsync(MarketCategory.Spot, CancellationToken.None);

        linear.Single(instrument => instrument.Symbol == "BTCUSDT").Perpetual.ShouldBeTrue();
        linear.Single(instrument => instrument.Symbol == "BTCUSDT-27DEC24").Perpetual.ShouldBeFalse();
        spot.ShouldAllBe(instrument => !instrument.Perpetual);
    }

    // The scanner loads the history of a younger pair from its launch; Bybit tells it for linear only.
    [Test]
    public async Task Linear_instrument_is_listed_with_its_launch_time_and_spot_without()
    {
        _factory.Bybit.LaunchTimes["SOLUSDT"] = new DateTimeOffset(2024, 2, 20, 8, 0, 0, TimeSpan.Zero);

        IReadOnlyList<ListedInstrument> linear = await Catalog().ListAsync(MarketCategory.Linear, CancellationToken.None);
        IReadOnlyList<ListedInstrument> spot = await Catalog().ListAsync(MarketCategory.Spot, CancellationToken.None);

        linear.Single(instrument => instrument.Symbol == "SOLUSDT").ListedAt.ShouldBe(new DateTimeOffset(2024, 2, 20, 8, 0, 0, TimeSpan.Zero));
        spot.ShouldAllBe(instrument => instrument.ListedAt == null);
    }

    // Scenario "Bybit не отвечает".
    [TestCase(MarketCategory.Spot)]
    [TestCase(MarketCategory.Linear)]
    public async Task Unavailable_bybit_means_the_exchange_is_unavailable(MarketCategory category)
    {
        _factory.Bybit.Unavailable = true;

        await Should.ThrowAsync<ExchangeUnavailableException>(() => Catalog().ListAsync(category, CancellationToken.None));
    }

    private BybitMarketDataClient Catalog()
    {
        return new BybitMarketDataClient(_factory.Services.GetRequiredService<IBybitRestClient>(), _factory.Services.GetRequiredService<ExchangeResilienceExecutor>());
    }
}