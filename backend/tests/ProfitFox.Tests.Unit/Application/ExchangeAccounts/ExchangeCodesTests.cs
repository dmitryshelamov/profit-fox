using ProfitFox.Application.ExchangeAccounts;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.ExchangeAccounts;

public class ExchangeCodesTests
{
    [TestCase("bybit", Exchange.Bybit)]
    [TestCase("tinvest", Exchange.TInvest)]
    public void Exchange_code_maps_both_ways(string code, Exchange exchange)
    {
        ExchangeCodes.TryParse(code, out Exchange parsed).ShouldBeTrue();
        parsed.ShouldBe(exchange);
        exchange.ToCode().ShouldBe(code);
    }

    [Test]
    public void Unknown_exchange_is_rejected()
    {
        ExchangeCodes.TryParse("binance", out _).ShouldBeFalse();
    }
}