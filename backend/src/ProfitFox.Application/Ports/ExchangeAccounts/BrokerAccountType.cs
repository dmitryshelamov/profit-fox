namespace ProfitFox.Application.Ports.ExchangeAccounts;

/// <summary>The type of a brokerage account: an ordinary one, an individual investment account (IIS) or another kind the system does not trade on.</summary>
public enum BrokerAccountType
{
    Brokerage,
    Iis,
    Other
}