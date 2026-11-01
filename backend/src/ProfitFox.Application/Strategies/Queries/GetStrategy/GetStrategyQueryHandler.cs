using MediatR;

using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Strategies.Queries.GetStrategy;

public sealed class GetStrategyQueryHandler(IStrategyLibraryRepository repository) : IRequestHandler<GetStrategyQuery, GetStrategyResult?>
{
    /// <summary>Every saved strategy has a version: the startup step gives one to older strategies before requests are served.</summary>
    public async Task<GetStrategyResult?> Handle(GetStrategyQuery request, CancellationToken cancellationToken)
    {
        Strategy? strategy = await repository.GetStrategyAsync(request.Id, cancellationToken);

        return strategy is null ? null : new GetStrategyResult(strategy, (await repository.GetLatestVersionNumberAsync(request.Id, cancellationToken))!.Value);
    }
}