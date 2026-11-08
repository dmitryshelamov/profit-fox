using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Entities.ExchangeAccounts;

/// <summary>Decision 2: one flat record whose constructor keeps the fields of each exchange.</summary>
public class ExchangeAccountTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:00:00Z");

    [Test]
    public void Bybit_account_has_a_key_and_a_uid()
    {
        var account = new ExchangeAccount(Guid.NewGuid(), "Main", "key", 100, new HashSet<MarketCategory> { MarketCategory.Spot }, Now);

        (account.Exchange, account.ApiKey, account.Uid, account.AccountId).ShouldBe((Exchange.Bybit, "key", 100L, null));
    }

    [Test]
    public void Bybit_account_without_a_key_is_refused()
    {
        Should.Throw<ArgumentException>(() => new ExchangeAccount(Guid.NewGuid(), "Main", Exchange.Bybit, null, 100, null, new HashSet<MarketCategory> { MarketCategory.Spot }, Now));
        Should.Throw<ArgumentException>(() => new ExchangeAccount(Guid.NewGuid(), "Main", Exchange.Bybit, "key", null, null, new HashSet<MarketCategory> { MarketCategory.Spot }, Now));
    }

    [Test]
    public void TInvest_account_has_the_number_of_the_brokerage_account()
    {
        var account = new ExchangeAccount(Guid.NewGuid(), "Брокерский", Exchange.TInvest, null, null, "2000000001", new HashSet<MarketCategory> { MarketCategory.Moex }, Now);

        (account.Exchange, account.ApiKey, account.Uid, account.AccountId).ShouldBe((Exchange.TInvest, null, null, "2000000001"));
    }

    [Test]
    public void TInvest_account_without_an_account_number_is_refused()
    {
        Should.Throw<ArgumentException>(() => new ExchangeAccount(Guid.NewGuid(), "Брокерский", Exchange.TInvest, null, null, null, new HashSet<MarketCategory> { MarketCategory.Moex }, Now));
        Should.Throw<ArgumentException>(() => new ExchangeAccount(Guid.NewGuid(), "Брокерский", Exchange.TInvest, null, null, " ", new HashSet<MarketCategory> { MarketCategory.Moex }, Now));
    }
}