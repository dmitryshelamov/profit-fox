namespace ProfitFox.Application.Ports.ExchangeAccounts;

/// <summary>What a token may do on an account: everything, read only, or nothing.</summary>
public enum BrokerAccountAccess
{
    Full,
    ReadOnly,
    None
}