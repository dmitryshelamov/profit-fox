using ProfitFox.Application.Ports.ExchangeAccounts;

namespace ProfitFox.Application.ExchangeAccounts;

/// <summary>Codes of the type, status and access of a brokerage account in the API: <c>brokerage</c>, <c>iis</c>, <c>other</c>; <c>new</c>, <c>open</c>, <c>closed</c>; <c>full</c>, <c>read_only</c>, <c>none</c>.</summary>
public static class BrokerAccountCodes
{
    public static string ToCode(this BrokerAccountType type)
    {
        return type switch
        {
            BrokerAccountType.Brokerage => "brokerage",
            BrokerAccountType.Iis => "iis",
            _ => "other"
        };
    }

    public static string ToCode(this BrokerAccountStatus status)
    {
        return status switch
        {
            BrokerAccountStatus.New => "new",
            BrokerAccountStatus.Open => "open",
            _ => "closed"
        };
    }

    public static string ToCode(this BrokerAccountAccess access)
    {
        return access switch
        {
            BrokerAccountAccess.Full => "full",
            BrokerAccountAccess.ReadOnly => "read_only",
            _ => "none"
        };
    }
}