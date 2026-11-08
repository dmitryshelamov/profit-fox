using Grpc.Core;

using ProfitFox.Application.Exceptions;
using ProfitFox.Infrastructure.Exchanges.TInvest;

using Shouldly;

using Tinkoff.InvestApi.V1;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.TInvest;

/// <summary>Decision 7: a ticker resolves into a share of TQBR once an hour; only a share in roubles that trades through the API counts.</summary>
public class TInvestSharesTests
{
    private TInvestTestHost _host = null!;

    [SetUp]
    public void SetUp()
    {
        _host = new TInvestTestHost();
    }

    [TearDown]
    public void TearDown()
    {
        _host.Dispose();
    }

    [Test]
    public async Task Share_is_resolved_on_tqbr_once_an_hour()
    {
        _host.Api.On<InstrumentRequest, ShareResponse>("ShareBy", _ => new ShareResponse { Instrument = TInvestFixtures.Sber() });

        TInvestShare first = await _host.Shares.ShareByAsync("SBER", CancellationToken.None);
        _host.Time.Advance(TInvestShares.Lifetime - TimeSpan.FromTicks(1));
        TInvestShare second = await _host.Shares.ShareByAsync("SBER", CancellationToken.None);
        _host.Time.Advance(TimeSpan.FromTicks(1));
        await _host.Shares.ShareByAsync("SBER", CancellationToken.None);

        first.ShouldBe(new TInvestShare(TInvestFixtures.SberUid, "SBER", "Сбер Банк", 10, 0.01m, ShortEnabled: true, TInvestFixtures.SberExchange));
        second.ShouldBe(first);
        _host.Api.Calls.Count.ShouldBe(2);
        var request = (InstrumentRequest)_host.Api.Calls[0].Request;
        (request.IdType, request.ClassCode, request.Id).ShouldBe((InstrumentIdType.Ticker, "TQBR", "SBER"));
    }

    [Test]
    public async Task Share_in_another_currency_is_refused()
    {
        _host.Api.On<InstrumentRequest, ShareResponse>("ShareBy", _ => new ShareResponse { Instrument = TInvestFixtures.Sber(currency: "usd") });

        await Should.ThrowAsync<ExchangeException>(() => _host.Shares.ShareByAsync("SBER", CancellationToken.None));
    }

    [Test]
    public async Task Share_closed_to_the_api_is_refused()
    {
        _host.Api.On<InstrumentRequest, ShareResponse>("ShareBy", _ => new ShareResponse { Instrument = TInvestFixtures.Sber(apiTrade: false) });

        await Should.ThrowAsync<ExchangeException>(() => _host.Shares.ShareByAsync("SBER", CancellationToken.None));
    }

    [TestCase(0, "0.01")]
    [TestCase(10, "0")]
    public async Task Share_without_a_positive_lot_or_price_step_is_refused(int lot, string step)
    {
        _host.Api.On<InstrumentRequest, ShareResponse>("ShareBy", _ => new ShareResponse { Instrument = TInvestFixtures.Sber(lot: lot, step: decimal.Parse(step, System.Globalization.CultureInfo.InvariantCulture)) });

        (await _host.Shares.TryShareByAsync("SBER", CancellationToken.None)).ShouldBeNull();
    }

    // Scenario "Акция не TQBR".
    [Test]
    public async Task Share_tinvest_does_not_list_is_none()
    {
        _host.Api.Fail("ShareBy", StatusCode.NotFound, "50002");

        (await _host.Shares.TryShareByAsync("NOPE", CancellationToken.None)).ShouldBeNull();
        await Should.ThrowAsync<ExchangeException>(() => _host.Shares.ShareByAsync("NOPE", CancellationToken.None));
    }

    [Test]
    public async Task Refused_share_is_not_kept()
    {
        _host.Api.On<InstrumentRequest, ShareResponse>("ShareBy", _ => new ShareResponse { Instrument = TInvestFixtures.Sber(apiTrade: false) });
        (await _host.Shares.TryShareByAsync("SBER", CancellationToken.None)).ShouldBeNull();
        _host.Api.Forget("ShareBy").On<InstrumentRequest, ShareResponse>("ShareBy", _ => new ShareResponse { Instrument = TInvestFixtures.Sber() });

        (await _host.Shares.TryShareByAsync("SBER", CancellationToken.None)).ShouldNotBeNull();
    }

    // Scenario "Нет аккаунта T-Invest".
    [Test]
    public async Task Without_a_tinvest_account_nothing_is_asked()
    {
        using var host = new TInvestTestHost(withAccount: false);
        host.Api.On<InstrumentRequest, ShareResponse>("ShareBy", _ => new ShareResponse { Instrument = TInvestFixtures.Sber() });

        ExchangeUnavailableException exception = await Should.ThrowAsync<ExchangeUnavailableException>(() => host.Shares.ShareByAsync("SBER", CancellationToken.None));

        exception.Message.ShouldBe(TInvestTokenSource.UnavailableMessage);
        host.Api.Calls.ShouldBeEmpty();
    }
}