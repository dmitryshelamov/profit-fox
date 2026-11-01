using MediatR;

using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Strategies.Queries.GetStrategyVersion;

public sealed class GetStrategyVersionQueryHandler(IStrategyLibraryRepository repository) : IRequestHandler<GetStrategyVersionQuery, StrategyVersion?>
{
    public Task<StrategyVersion?> Handle(GetStrategyVersionQuery request, CancellationToken cancellationToken)
    {
        return repository.GetVersionAsync(request.StrategyId, request.Number, cancellationToken);
    }
}