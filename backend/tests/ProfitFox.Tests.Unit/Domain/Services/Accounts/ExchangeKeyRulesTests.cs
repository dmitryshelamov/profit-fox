using ProfitFox.Domain.Services.Accounts;
using ProfitFox.Domain.ValueObjects.Accounts;

namespace ProfitFox.Tests.Unit.Domain.Services.Accounts;

/// <summary>
///     Change add-exchange-accounts, task 3.1 (design.md, decision 5): spec exchange-accounts «Проверка ключа у Bybit» — the
///     bench takes a key of a unified account that trades contracts, may not withdraw and is not read-only; the first broken
///     rule in the order of the design is the answer, at the field of the key.
/// </summary>
public sealed class ExchangeKeyRulesTests
{
    private static readonly ExchangeKeyInfo Trading = new ExchangeKeyInfo(104857321, false, true, ["AccountTransfer", "SubMemberTransfer"], ["Order", "Position"]);

    [Test]
    public void A_trading_key_of_a_unified_account_is_taken_with_or_without_spot()
    {
        Assert.That(ExchangeKeyRules.Check(Trading), Is.Null);
        Assert.That(ExchangeKeyRules.Check(Trading with { Wallet = [] }), Is.Null);
    }

    // Spec «Ключ с правом вывода».
    [Test]
    public void A_key_that_may_withdraw_is_refused()
    {
        Assert.That(ExchangeKeyRules.Check(Trading with { Wallet = ["AccountTransfer", "Withdraw"] }), Is.EqualTo(new AccountProblem("WithdrawPermission", "у ключа есть право вывода", "apiKey")));
    }

    // Spec «Ключ только для чтения».
    [Test]
    public void A_read_only_key_is_refused()
    {
        Assert.That(ExchangeKeyRules.Check(Trading with { ReadOnly = true }), Is.EqualTo(new AccountProblem("ReadOnlyKey", "ключ не может торговать: он только для чтения", "apiKey")));
    }

    // Spec «Нет права торговли бессрочными»: spot only, and orders without positions.
    [Test]
    public void A_key_without_both_orders_and_positions_of_contracts_is_refused()
    {
        var refused = new AccountProblem("NoContractTrade", "нет права торговли бессрочными контрактами", "apiKey");

        Assert.That(ExchangeKeyRules.Check(Trading with { ContractTrade = [] }), Is.EqualTo(refused));
        Assert.That(ExchangeKeyRules.Check(Trading with { ContractTrade = ["Order"] }), Is.EqualTo(refused));
        Assert.That(ExchangeKeyRules.Check(Trading with { ContractTrade = ["Position"] }), Is.EqualTo(refused));
    }

    // Spec «Не единый торговый аккаунт».
    [Test]
    public void A_key_of_a_classic_account_is_refused()
    {
        Assert.That(ExchangeKeyRules.Check(Trading with { Unified = false }), Is.EqualTo(new AccountProblem("NotUnifiedAccount", "нужен единый торговый аккаунт", "apiKey")));
    }

    [Test]
    public void With_several_rules_broken_the_first_of_the_order_is_the_answer()
    {
        var all = new ExchangeKeyInfo(1, true, false, ["Withdraw"], []);

        Assert.That(ExchangeKeyRules.Check(all)!.Code, Is.EqualTo("ReadOnlyKey"));
        Assert.That(ExchangeKeyRules.Check(all with { ReadOnly = false })!.Code, Is.EqualTo("NotUnifiedAccount"));
        Assert.That(ExchangeKeyRules.Check(all with { ReadOnly = false, Unified = true })!.Code, Is.EqualTo("WithdrawPermission"));
        Assert.That(ExchangeKeyRules.Check(all with { ReadOnly = false, Unified = true, Wallet = [] })!.Code, Is.EqualTo("NoContractTrade"));
    }
}
