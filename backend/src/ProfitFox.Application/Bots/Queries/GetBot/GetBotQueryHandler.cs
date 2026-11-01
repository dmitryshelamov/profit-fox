using MediatR;

using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Bots.Queries.GetBot;

public sealed class GetBotQueryHandler(IStrategyLibraryRepository repository) : IRequestHandler<GetBotQuery, Bot?>
{
    public Task<Bot?> Handle(GetBotQuery request, CancellationToken cancellationToken)
    {
        return repository.GetBotAsync(request.Id, cancellationToken);
    }
}