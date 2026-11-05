using MediatR;

using ProfitFox.Application.Ideas.Queries.GetIdeas;
using ProfitFox.Application.Market;
using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Ideas.Queries.GetPairIdeas;

public sealed class GetPairIdeasQueryHandler(IIdeaRepository ideas, IIdeaBucketRepository buckets, IdeasEngineVersion version, TimeProvider time)
    : IRequestHandler<GetPairIdeasQuery, GetPairIdeasResult>
{
    public async Task<GetPairIdeasResult> Handle(GetPairIdeasQuery request, CancellationToken cancellationToken)
    {
        if (!MarketDataCodes.TryParseCategory(request.Category, out MarketCategory category))
        {
            return new GetPairIdeasResult(null, new Dictionary<string, string> { ["category"] = MarketQueries.InvalidCategory });
        }

        IReadOnlyList<Idea> recent = await ideas.GetRecentAsync(time.GetUtcNow() - IdeaQueries.ClosedShown, new Instrument(category, request.Symbol), cancellationToken);

        return new GetPairIdeasResult(
            [.. GetIdeasQueryHandler.ByExpectancy(IdeaView.Of(recent, await buckets.GetAsync(version.Number, cancellationToken)))],
            new Dictionary<string, string>());
    }
}