using MediatR;

using ProfitFox.Application.Market;
using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;

namespace ProfitFox.Application.Ideas.Queries.GetIdeas;

public sealed class GetIdeasQueryHandler(IIdeaRepository ideas, IIdeaBucketRepository buckets, IdeasEngineVersion version, TimeProvider time)
    : IRequestHandler<GetIdeasQuery, GetIdeasResult>
{
    public async Task<GetIdeasResult> Handle(GetIdeasQuery request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        IdeaSetup setup = default;
        Interval interval = default;
        IdeaType type = default;
        IdeaSide side = default;
        MarketCategory category = default;

        if (request.Setup is not null && !IdeaCodes.TryParseSetup(request.Setup, out setup))
        {
            errors["setup"] = IdeaQueries.InvalidSetup;
        }

        if (request.Interval is not null && !IdeaQueries.TryParseInterval(request.Interval, out interval))
        {
            errors["interval"] = IdeaQueries.InvalidInterval;
        }

        if (request.Type is not null && !IdeaCodes.TryParseType(request.Type, out type))
        {
            errors["type"] = IdeaQueries.InvalidType;
        }

        if (request.Side is not null && !IdeaCodes.TryParseSide(request.Side, out side))
        {
            errors["side"] = IdeaQueries.InvalidSide;
        }

        if (request.Category is not null && !MarketDataCodes.TryParseCategory(request.Category, out category))
        {
            errors["category"] = MarketQueries.InvalidCategory;
        }

        if (errors.Count > 0)
        {
            return new GetIdeasResult(null, errors);
        }

        IReadOnlyList<Idea> recent = await ideas.GetRecentAsync(time.GetUtcNow() - IdeaQueries.ClosedShown, null, cancellationToken);
        IEnumerable<Idea> matching = recent.Where(idea =>
            (request.Setup is null || idea.Setup == setup)
            && (request.Interval is null || idea.Interval == interval)
            && (request.Type is null || idea.Track.Plan.Type == type)
            && (request.Side is null || idea.Track.Plan.Side == side)
            && (request.Category is null || idea.Instrument.Category == category));
        IEnumerable<IdeaView> views = IdeaView.Of(matching, await buckets.GetAsync(version.Number, cancellationToken));

        if (request.NonNegativeExpectancy)
        {
            views = views.Where(view => view.Bucket?.Expectancy >= 0);
        }

        return new GetIdeasResult([.. ByExpectancy(views)], errors);
    }

    /// <summary>Highest expectancy first, ideas of a bucket without cases last; newest first among equals.</summary>
    public static IEnumerable<IdeaView> ByExpectancy(IEnumerable<IdeaView> views)
    {
        return views.OrderByDescending(view => view.Bucket?.Expectancy ?? decimal.MinValue).ThenByDescending(view => view.Idea.Track.FiredAt);
    }
}