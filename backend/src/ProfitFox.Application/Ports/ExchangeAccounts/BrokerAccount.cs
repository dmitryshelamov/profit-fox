namespace ProfitFox.Application.Ports.ExchangeAccounts;

/// <summary>A brokerage account a T-Invest token opens: its number, name, type, status and what the token may do on it.</summary>
public sealed record BrokerAccount(string Id, string Name, BrokerAccountType Type, BrokerAccountStatus Status, BrokerAccountAccess Access);