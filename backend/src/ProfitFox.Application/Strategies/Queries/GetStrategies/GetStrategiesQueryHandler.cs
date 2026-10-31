using MediatR;

using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Strategies.Queries.GetStrategies;

public sealed class GetStrategiesQueryHandler(IStrategyLibraryRepository repository) : IRequestHandler<GetStrategiesQuery, IReadOnlyList<Strategy>>
{
    public Task<IReadOnlyList<Strategy>> Handle(GetStrategiesQuery request, CancellationToken cancellationToken)
    {
        return repository.GetStrategiesAsync(cancellationToken);
    }
}