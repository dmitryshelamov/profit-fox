using ProfitFox.Application.Ports.ExchangeAccounts;

namespace ProfitFox.Application.ExchangeAccounts;

/// <summary>Which accounts of a T-Invest token can be added: an open brokerage account or IIS the token has full access to.</summary>
public static class BrokerAccountRules
{
    public const string NotOpen = "not_open";
    public const string NotFullAccess = "not_full_access";
    public const string UnsupportedType = "unsupported_type";

    /// <summary>The code of why the account cannot be added, or <c>null</c> when it can.</summary>
    public static string? Problem(BrokerAccount account)
    {
        if (account.Type == BrokerAccountType.Other)
        {
            return UnsupportedType;
        }

        if (account.Status != BrokerAccountStatus.Open)
        {
            return NotOpen;
        }

        return account.Access == BrokerAccountAccess.Full ? null : NotFullAccess;
    }

    /// <summary>What the user is told about an account that cannot be added.</summary>
    public static string Describe(string problem)
    {
        return problem switch
        {
            NotOpen => "The account is not open.",
            NotFullAccess => "The token has read-only access to the account.",
            _ => "Only a brokerage account or an IIS can be added."
        };
    }
}