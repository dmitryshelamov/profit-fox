using MediatR;

using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Bots.Queries.GetBots;

public sealed class GetBotsQueryHandler(IStrategyLibraryRepository repository) : IRequestHandler<GetBotsQuery, IReadOnlyList<Bot>>
{
    public Task<IReadOnlyList<Bot>> Handle(GetBotsQuery request, CancellationToken cancellationToken)
    {
        return repository.GetBotsAsync(cancellationToken);
    }
}