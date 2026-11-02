using MediatR;

namespace ProfitFox.Application.ExchangeAccounts.Queries.GetExchangeAccount;

public sealed record GetExchangeAccountQuery(Guid Id) : IRequest<GetExchangeAccountResult?>;