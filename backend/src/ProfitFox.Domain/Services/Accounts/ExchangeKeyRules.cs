using ProfitFox.Domain.ValueObjects.Accounts;

namespace ProfitFox.Domain.Services.Accounts;

/// <summary>
///     Which key of Bybit the bench takes (change add-exchange-accounts, design.md, decision 5; spec exchange-accounts
///     «Проверка ключа у Bybit»): a key of a unified trading account that may trade contracts — place orders and manage
///     positions — that may not withdraw and is not read-only. The rules are checked in this order, and the first broken one
///     is the answer: a read-only key, an account that is not unified, the permission to withdraw, no trading of contracts.
///     The permissions of spot are not needed and do no harm.
/// </summary>
public static class ExchangeKeyRules
{
    /// <summary>
    ///     Why the key is refused, at the field of the key; null when it is taken.
    /// </summary>
    public static AccountProblem? Check(ExchangeKeyInfo key)
    {
        if (key.ReadOnly)
        {
            return Refused("ReadOnlyKey", "ключ не может торговать: он только для чтения");
        }

        if (!key.Unified)
        {
            return Refused("NotUnifiedAccount", "нужен единый торговый аккаунт");
        }

        if (key.Wallet.Contains("Withdraw", StringComparer.Ordinal))
        {
            return Refused("WithdrawPermission", "у ключа есть право вывода");
        }

        if (!key.ContractTrade.Contains("Order", StringComparer.Ordinal) || !key.ContractTrade.Contains("Position", StringComparer.Ordinal))
        {
            return Refused("NoContractTrade", "нет права торговли бессрочными контрактами");
        }

        return null;
    }

    private static AccountProblem Refused(string code, string reason)
    {
        return new AccountProblem(code, reason, "apiKey");
    }
}
