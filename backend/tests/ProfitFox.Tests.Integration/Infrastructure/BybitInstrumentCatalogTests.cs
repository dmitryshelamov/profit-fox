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
            new ListedInstrument(MarketCategory.Spot, "BTCUSDT", "BTC", "USDT"),
            new ListedInstrument(MarketCategory.Spot, "ETHUSDT", "ETH", "USDT")
        ]);
    }

    [Test]
    public async Task Linear_list_follows_the_cursor_and_collects_every_page()
    {
        _factory.Bybit.LinearPageSize = 2;

        IReadOnlyList<ListedInstrument> listed = await Catalog().ListAsync(MarketCategory.Linear, CancellationToken.None);

        listed.Select(instrument => instrument.Symbol).ShouldBe(["ADAUSDT", "BTCUSDT", "ETHUSDT", "SOLUSDT", "XRPUSDT"]);
        listed.ShouldAllBe(instrument => instrument.Category == MarketCategory.Linear);
        listed.Single(instrument => instrument.Symbol == "SOLUSDT").ShouldBe(new ListedInstrument(MarketCategory.Linear, "SOLUSDT", "SOL", "USDT"));
        _factory.Bybit.ListRequests.Count.ShouldBe(3);
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