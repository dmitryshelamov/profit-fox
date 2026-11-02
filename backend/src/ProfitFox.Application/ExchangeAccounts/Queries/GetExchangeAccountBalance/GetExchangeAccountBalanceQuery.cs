using MediatR;

using ProfitFox.Application.Ports.ExchangeAccounts;

namespace ProfitFox.Application.ExchangeAccounts.Queries.GetExchangeAccountBalance;

/// <summary>The balance of the account read from the exchange now; <c>null</c> when there is no such account.</summary>
public sealed record GetExchangeAccountBalanceQuery(Guid Id) : IRequest<AccountBalance?>;