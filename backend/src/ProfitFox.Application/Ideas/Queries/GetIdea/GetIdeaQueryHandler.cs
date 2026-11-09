using MediatR;

using ProfitFox.Application.Backtests;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;

namespace ProfitFox.Application.Ideas.Queries.GetIdea;

public sealed class GetIdeaQueryHandler(IIdeaRepository ideas, IIdeaBucketRepository buckets, IdeasEngineVersion version, BacktestSettings backtests) : IRequestHandler<GetIdeaQuery, IdeaView?>
{
    public async Task<IdeaView?> Handle(GetIdeaQuery request, CancellationToken cancellationToken)
    {
        Idea? idea = await ideas.GetAsync(request.Id, cancellationToken);

        return idea is null ? null : IdeaView.Of([idea], await buckets.GetAsync(version.Number, cancellationToken), await buckets.GetCurvesAsync(version.Number, cancellationToken), backtests.Policy().DefaultFees)[0];
    }
}