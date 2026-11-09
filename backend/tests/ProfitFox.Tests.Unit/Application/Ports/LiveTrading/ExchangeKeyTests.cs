using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Ports.LiveTrading;

/// <summary>Decision 2: a key names its exchange, and its text never shows the secret.</summary>
public class ExchangeKeyTests
{
    [Test]
    public void Key_is_of_bybit_unless_told()
    {
        new ExchangeKey("api-key", "api-secret").Exchange.ShouldBe(Exchange.Bybit);
    }

    // On T-Invest the key is the number of the brokerage account and the token is the secret.
    [Test]
    public void Key_of_tinvest_prints_the_account_and_the_exchange_but_not_the_token()
    {
        var key = new ExchangeKey("2000000001", "t.secret-token", Exchange.TInvest);

        key.ToString().ShouldBe("ExchangeKey { ApiKey = 2000000001, Exchange = TInvest }");
        key.ToString().ShouldNotContain("t.secret-token");
    }
}