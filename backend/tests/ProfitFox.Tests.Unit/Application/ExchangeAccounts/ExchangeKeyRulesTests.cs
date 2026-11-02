using ProfitFox.Application.ExchangeAccounts;
using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Domain.Entities.MarketData.Enums;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.ExchangeAccounts;

public class ExchangeKeyRulesTests
{
    private static readonly ExchangeKeyInfo Valid = new ExchangeKeyInfo(
        100,
        IsReadOnly: false,
        IsUnifiedAccount: true,
        CanWithdraw: false,
        new HashSet<MarketCategory> { MarketCategory.Spot, MarketCategory.Linear });

    [Test]
    public void Trading_key_without_withdrawal_is_accepted()
    {
        ExchangeKeyRules.Check(Valid).ShouldBeEmpty();
    }

    [Test]
    public void Spot_only_key_is_accepted()
    {
        ExchangeKeyRules.Check(Valid with { TradableCategories = new HashSet<MarketCategory> { MarketCategory.Spot } }).ShouldBeEmpty();
    }

    // Scenario "Ключ только для чтения".
    [Test]
    public void Read_only_key_is_rejected()
    {
        ExchangeKeyRules.Check(Valid with { IsReadOnly = true }).ShouldBe(["The key is read-only; a bot needs a key that can trade."]);
    }

    // Scenario "Ключ не единого торгового аккаунта".
    [Test]
    public void Key_of_a_classic_account_is_rejected()
    {
        ExchangeKeyRules.Check(Valid with { IsUnifiedAccount = false }).ShouldBe(["The key belongs to a classic account; only a unified trading account is supported."]);
    }

    // Scenario "Ключ с правом на вывод средств".
    [Test]
    public void Key_that_can_withdraw_is_rejected()
    {
        ExchangeKeyRules.Check(Valid with { CanWithdraw = true }).ShouldBe(["The key can withdraw funds; create a key without the withdrawal permission."]);
    }

    [Test]
    public void Key_that_trades_nowhere_is_rejected()
    {
        ExchangeKeyRules.Check(Valid with { TradableCategories = new HashSet<MarketCategory>() }).ShouldBe(["The key can trade neither spot nor linear contracts."]);
    }

    [Test]
    public void Every_problem_is_reported_in_order()
    {
        ExchangeKeyInfo key = Valid with { IsReadOnly = true, IsUnifiedAccount = false, CanWithdraw = true, TradableCategories = new HashSet<MarketCategory>() };

        ExchangeKeyRules.Check(key).Count.ShouldBe(4);
        ExchangeKeyRules.Check(key)[0].ShouldStartWith("The key is read-only");
        ExchangeKeyRules.Check(key)[3].ShouldStartWith("The key can trade neither");
    }
}