using MediatR;

using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.ExchangeAccounts.Queries.GetExchangeAccounts;

public sealed class GetExchangeAccountsQueryHandler(IExchangeAccountRepository repository) : IRequestHandler<GetExchangeAccountsQuery, IReadOnlyList<ExchangeAccount>>
{
    public Task<IReadOnlyList<ExchangeAccount>> Handle(GetExchangeAccountsQuery request, CancellationToken cancellationToken)
    {
        return repository.GetAllAsync(cancellationToken);
    }
}