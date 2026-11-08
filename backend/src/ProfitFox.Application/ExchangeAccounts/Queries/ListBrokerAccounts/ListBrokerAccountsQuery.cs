using MediatR;

namespace ProfitFox.Application.ExchangeAccounts.Queries.ListBrokerAccounts;

/// <summary>The accounts a T-Invest token opens, each with whether it can be added; the token is stored nowhere.</summary>
public sealed record ListBrokerAccountsQuery(string Token) : IRequest<IReadOnlyList<BrokerAccountOption>>;