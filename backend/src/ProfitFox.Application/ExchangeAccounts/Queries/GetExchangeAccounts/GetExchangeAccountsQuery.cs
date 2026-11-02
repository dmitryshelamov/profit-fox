using MediatR;

using ProfitFox.Domain.Entities.ExchangeAccounts;

namespace ProfitFox.Application.ExchangeAccounts.Queries.GetExchangeAccounts;

public sealed record GetExchangeAccountsQuery : IRequest<IReadOnlyList<ExchangeAccount>>;