using ProfitFox.Application.ExchangeAccounts;
using ProfitFox.Application.Ports.ExchangeAccounts;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.ExchangeAccounts;

/// <summary>Requirement "Счета токена T-Invest": only an open brokerage account or IIS with full access can be saved.</summary>
public class BrokerAccountRulesTests
{
    [Test]
    public void Open_brokerage_account_with_full_access_can_be_saved()
    {
        BrokerAccountRules.Problem(Account(BrokerAccountType.Brokerage, BrokerAccountStatus.Open, BrokerAccountAccess.Full)).ShouldBeNull();
        BrokerAccountRules.Problem(Account(BrokerAccountType.Iis, BrokerAccountStatus.Open, BrokerAccountAccess.Full)).ShouldBeNull();
    }

    // Scenario "Два счёта токена": a closed IIS is not open.
    [TestCase(BrokerAccountStatus.Closed)]
    [TestCase(BrokerAccountStatus.New)]
    public void Account_that_is_not_open_cannot_be_saved(BrokerAccountStatus status)
    {
        BrokerAccountRules.Problem(Account(BrokerAccountType.Iis, status, BrokerAccountAccess.Full)).ShouldBe("not_open");
    }

    // Scenario "Доступ отозван между выбором и сохранением".
    [TestCase(BrokerAccountAccess.ReadOnly)]
    [TestCase(BrokerAccountAccess.None)]
    public void Account_without_full_access_cannot_be_saved(BrokerAccountAccess access)
    {
        BrokerAccountRules.Problem(Account(BrokerAccountType.Brokerage, BrokerAccountStatus.Open, access)).ShouldBe("not_full_access");
    }

    [Test]
    public void Account_of_another_type_cannot_be_saved()
    {
        BrokerAccountRules.Problem(Account(BrokerAccountType.Other, BrokerAccountStatus.Open, BrokerAccountAccess.Full)).ShouldBe("unsupported_type");
    }

    private static BrokerAccount Account(BrokerAccountType type, BrokerAccountStatus status, BrokerAccountAccess access)
    {
        return new BrokerAccount("2000000001", "Брокерский счёт", type, status, access);
    }
}