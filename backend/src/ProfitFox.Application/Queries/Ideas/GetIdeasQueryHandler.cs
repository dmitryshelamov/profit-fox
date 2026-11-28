using MediatR;

using ProfitFox.Application.Queries.Filters;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Lists;
using ProfitFox.Domain.ValueObjects.Lists;
using ProfitFox.Domain.ValueObjects.Lists.Enums;

namespace ProfitFox.Application.Queries.Ideas;

/// <summary>
///     The lists of ideas: sorted by the moment from the newest by default, the journal by the exit; the saved filter is one
///     of «Идеи». A page names the trade of each idea that has one (change take-idea-trades).
/// </summary>
internal sealed class GetIdeasQueryHandler(IIdeaRepository ideas, ISavedFilterRepository filters, ITradeRepository trades)
    : IRequestHandler<GetIdeasQuery, IdeaListResult>, IRequestHandler<GetIdeaSummaryQuery, IdeaSummaryResult>, IRequestHandler<GetIdeaScoresQuery, IdeaScoresResult>
{
    public async Task<IdeaListResult> Handle(GetIdeasQuery request, CancellationToken cancellationToken)
    {
        ListOrder order = FilterResolver.Order(ListFields.Ideas, request.Kind == IdeaListKind.Closed ? "exitTime" : "moment", request.List);
        (IReadOnlyList<FilterCondition> conditions, bool gone) = await FilterResolver.ConditionsAsync(filters, request.List.Filter, FilterPlace.Ideas, cancellationToken);
        ListPage<Domain.Entities.Ideas.Idea> page = await ideas.ListAsync(Query(request, order, conditions), cancellationToken);
        return new IdeaListResult(page, gone) { Trades = await trades.ByIdeasAsync([.. page.Items.Select(idea => idea.Id)], cancellationToken) };
    }

    public async Task<IdeaSummaryResult> Handle(GetIdeaSummaryQuery request, CancellationToken cancellationToken)
    {
        ListOrder order = FilterResolver.Order(ListFields.Summary, "ideas", request.Ideas.List);
        (IReadOnlyList<FilterCondition> conditions, bool gone) = await FilterResolver.ConditionsAsync(filters, request.Ideas.List.Filter, FilterPlace.Ideas, cancellationToken);
        return new IdeaSummaryResult(await ideas.SummaryAsync(Query(request.Ideas with { Kind = IdeaListKind.Closed }, order, conditions), cancellationToken), gone);
    }

    public async Task<IdeaScoresResult> Handle(GetIdeaScoresQuery request, CancellationToken cancellationToken)
    {
        ListOrder order = FilterResolver.Order(ListFields.Ideas, "exitTime", request.Ideas.List with { Sort = null, Direction = null, Page = null, PageSize = null });
        (IReadOnlyList<FilterCondition> conditions, bool gone) = await FilterResolver.ConditionsAsync(filters, request.Ideas.List.Filter, FilterPlace.Ideas, cancellationToken);
        return new IdeaScoresResult(await ideas.ScoresAsync(Query(request.Ideas with { Kind = IdeaListKind.Closed }, order, conditions), cancellationToken), gone);
    }

    private static IdeaListQuery Query(GetIdeasQuery request, ListOrder order, IReadOnlyList<FilterCondition> conditions)
    {
        return new IdeaListQuery(
            request.Kind,
            order,
            request.Symbol,
            request.Search,
            request.Strategy,
            request.Timeframe,
            request.Side,
            request.Label,
            request.OnlyPositive,
            request.OnlyWithStop,
            request.ExitReason,
            request.PeriodDays,
            request.State,
            conditions,
            request.From,
            request.To,
            request.Around,
            request.Kind == IdeaListKind.Pair ? null : FilterResolver.Kind(request.PairKind),
            FilterResolver.MinScore(request.MinScore));
    }
}
