using MediatR;

using ProfitFox.Domain.Entities.Accounts;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Queries.Accounts;

/// <summary>
///     The accounts of the exchange in the order they were added (spec exchange-accounts «Список аккаунтов»), without their
///     secrets.
/// </summary>
public sealed record GetAccountsQuery : IRequest<IReadOnlyList<ExchangeAccount>>;

internal sealed class GetAccountsQueryHandler(IExchangeAccountRepository accounts) : IRequestHandler<GetAccountsQuery, IReadOnlyList<ExchangeAccount>>
{
    public Task<IReadOnlyList<ExchangeAccount>> Handle(GetAccountsQuery request, CancellationToken cancellationToken)
    {
        return accounts.AllAsync(cancellationToken);
    }
}
