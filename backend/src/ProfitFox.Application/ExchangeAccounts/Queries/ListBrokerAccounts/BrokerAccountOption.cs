using ProfitFox.Application.Ports.ExchangeAccounts;

namespace ProfitFox.Application.ExchangeAccounts.Queries.ListBrokerAccounts;

/// <summary>An account of a token and the code of why it cannot be added (<see cref="BrokerAccountRules" />), <c>null</c> when it can.</summary>
public sealed record BrokerAccountOption(BrokerAccount Account, string? Problem);