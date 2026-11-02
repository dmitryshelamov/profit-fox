using ProfitFox.Application.Ports.ExchangeAccounts;

namespace ProfitFox.Application.ExchangeAccounts;

/// <summary>Which keys an account accepts: a key a bot can trade with and that cannot take money out of the account.</summary>
public static class ExchangeKeyRules
{
    public static IReadOnlyList<string> Check(ExchangeKeyInfo key)
    {
        var problems = new List<string>();

        if (key.IsReadOnly)
        {
            problems.Add("The key is read-only; a bot needs a key that can trade.");
        }

        if (!key.IsUnifiedAccount)
        {
            problems.Add("The key belongs to a classic account; only a unified trading account is supported.");
        }

        if (key.CanWithdraw)
        {
            problems.Add("The key can withdraw funds; create a key without the withdrawal permission.");
        }

        if (key.TradableCategories.Count == 0)
        {
            problems.Add("The key can trade neither spot nor linear contracts.");
        }

        return problems;
    }
}