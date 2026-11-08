using Grpc.Core;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Exchanges.TInvest;

using Shouldly;

using Tinkoff.InvestApi.V1;

using Instrument = ProfitFox.Domain.ValueObjects.MarketData.Instrument;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.TInvest;

/// <summary>Requirements "Акции MOEX в поиске", "Правила акции" and the prices of a share through T-Invest.</summary>
public class TInvestMarketDataClientTests
{
    private static readonly Instrument Sber = new Instrument(MarketCategory.Moex, "SBER");

    private TInvestTestHost _host = null!;

    [SetUp]
    public void SetUp()
    {
        _host = new TInvestTestHost();
        _host.Api.On<InstrumentRequest, ShareResponse>("ShareBy", request => request.Id == "SBER"
            ? new ShareResponse { Instrument = TInvestFixtures.Sber() }
            : throw new RpcException(new Status(StatusCode.NotFound, "50002")));
    }

    [TearDown]
    public void TearDown()
    {
        _host.Dispose();
    }

    // Scenario "Акции MOEX в поиске": the shares of TQBR in roubles that trade through the API, with their lot as step and minimum.
    [Test]
    public async Task Catalog_lists_the_tradable_shares_of_tqbr()
    {
        _host.Api.On<InstrumentsRequest, SharesResponse>(
            "Shares",
            _ => new SharesResponse
            {
                Instruments =
                {
                    TInvestFixtures.Sber(),
                    TInvestFixtures.Sber(classCode: "SPBXM"),
                    TInvestFixtures.Sber(currency: "usd"),
                    TInvestFixtures.Sber(apiTrade: false),
                    TInvestFixtures.Sber(lot: 0)
                }
            });

        IReadOnlyList<ListedInstrument> listed = await _host.MarketData.ListAsync(MarketCategory.Moex, CancellationToken.None);

        listed.ShouldBe(
        [
            new ListedInstrument(
                MarketCategory.Moex,
                "SBER",
                "SBER",
                "RUB",
                Trading: true,
                TickSize: 0.01m,
                TradFi: true,
                QuantityStep: 10m,
                MinQuantity: 10m,
                MinAmount: 0m,
                Name: "Сбер Банк",
                ShortEnabled: true)
        ]);
        ((InstrumentsRequest)_host.Api.Calls.Single().Request).InstrumentStatus.ShouldBe(InstrumentStatus.Base);
    }

    // Scenarios "Акция MOEX" and "Акция не TQBR".
    [Test]
    public async Task Share_of_tqbr_exists_and_another_ticker_does_not()
    {
        (await _host.MarketData.InstrumentExistsAsync(Sber, CancellationToken.None)).ShouldBeTrue();
        (await _host.MarketData.InstrumentExistsAsync(new Instrument(MarketCategory.Moex, "NOPE"), CancellationToken.None)).ShouldBeFalse();
    }

    // Scenario "Правила акции".
    [Test]
    public async Task Rules_of_a_share_are_its_lot_and_price_step()
    {
        (await _host.MarketData.GetRulesAsync(Sber, CancellationToken.None)).ShouldBe(new InstrumentRules(0.01m, 10m, 10m, 0m));
    }

    [Test]
    public async Task Last_price_of_a_share_is_asked_by_its_uid()
    {
        _host.Api.On<GetLastPricesRequest, GetLastPricesResponse>(
            "GetLastPrices",
            _ => new GetLastPricesResponse { LastPrices = { new LastPrice { InstrumentUid = TInvestFixtures.SberUid, Price = 305.5m.ToQuotation() } } });

        (await _host.MarketData.GetLastPriceAsync(Sber, CancellationToken.None)).ShouldBe(305.5m);

        ((GetLastPricesRequest)_host.Api.Calls.Last().Request).InstrumentId.ShouldBe([TInvestFixtures.SberUid]);
    }

    [Test]
    public async Task Share_without_a_last_price_is_unavailable()
    {
        _host.Api.On<GetLastPricesRequest, GetLastPricesResponse>("GetLastPrices", _ => new GetLastPricesResponse());

        await Should.ThrowAsync<ExchangeUnavailableException>(() => _host.MarketData.GetLastPriceAsync(Sber, CancellationToken.None));
    }

    [Test]
    public async Task Last_prices_of_the_category_come_by_ticker()
    {
        _host.Api.On<InstrumentsRequest, SharesResponse>("Shares", _ => new SharesResponse { Instruments = { TInvestFixtures.Sber() } });
        _host.Api.On<GetLastPricesRequest, GetLastPricesResponse>(
            "GetLastPrices",
            _ => new GetLastPricesResponse
            {
                LastPrices =
                {
                    new LastPrice { InstrumentUid = TInvestFixtures.SberUid, Price = 305.5m.ToQuotation() },
                    new LastPrice { InstrumentUid = "other-uid", Price = 1m.ToQuotation() }
                }
            });

        IReadOnlyDictionary<string, decimal> prices = await _host.MarketData.GetLastPricesAsync(MarketCategory.Moex, CancellationToken.None);

        prices.ShouldBe(new Dictionary<string, decimal> { ["SBER"] = 305.5m });
    }

    // Scenario "Данные T-Invest недоступны".
    [Test]
    public async Task Without_a_tinvest_account_the_catalog_is_unavailable_and_nothing_is_asked()
    {
        using var host = new TInvestTestHost(withAccount: false);
        host.Api.On<InstrumentsRequest, SharesResponse>("Shares", _ => new SharesResponse { Instruments = { TInvestFixtures.Sber() } });

        await Should.ThrowAsync<ExchangeUnavailableException>(() => host.MarketData.ListAsync(MarketCategory.Moex, CancellationToken.None));

        host.Api.Calls.ShouldBeEmpty();
    }
}