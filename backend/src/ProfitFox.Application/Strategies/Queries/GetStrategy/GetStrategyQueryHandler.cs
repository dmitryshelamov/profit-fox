using MediatR;

using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Strategies.Queries.GetStrategy;

public sealed class GetStrategyQueryHandler(IStrategyLibraryRepository repository) : IRequestHandler<GetStrategyQuery, Strategy?>
{
    public Task<Strategy?> Handle(GetStrategyQuery request, CancellationToken cancellationToken)
    {
        return repository.GetStrategyAsync(request.Id, cancellationToken);
    }
}