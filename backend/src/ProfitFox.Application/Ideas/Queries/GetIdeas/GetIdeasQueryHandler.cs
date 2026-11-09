using MediatR;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.Market;
using ProfitFox.Application.MarketData;
using ProfitFox.Application.Paging;
using ProfitFox.Application.SavedFilters;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.SavedFilters;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Ideas.Queries.GetIdeas;

public sealed class GetIdeasQueryHandler(
    IIdeaRepository ideas,
    IIdeaBucketRepository buckets,
    IMarketSnapshotRepository snapshots,
    ISavedFilterRepository filters,
    IScannerPairRepository pairs,
    IdeasEngineVersion version,
    BacktestSettings backtests,
    TimeProvider time) : IRequestHandler<GetIdeasQuery, GetIdeasResult>
{
    public async Task<GetIdeasResult> Handle(GetIdeasQuery request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        IdeaSetup setup = default;
        Interval interval = default;
        IdeaType type = default;
        IdeaSide side = default;
        MarketCategory category = default;
        IdeaSortColumn column = IdeaSortColumn.Expectancy;
        OrderDirection direction = OrderDirection.Descending;

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

        if (request.Sort is not null && !IdeaOrder.TryParseColumn(request.Sort, out column))
        {
            errors["sort"] = IdeaOrder.InvalidColumn;
        }

        if (request.Direction is not null && !OrderDirections.TryParse(request.Direction, out direction))
        {
            errors["direction"] = OrderDirections.Invalid;
        }

        var page = PageRequest.Parse(request.Page, request.PageSize, errors);
        SavedFilter? filter = null;

        if (request.SavedFilter is { } filterId)
        {
            filter = await filters.GetAsync(filterId, cancellationToken);

            if (filter is null)
            {
                errors["savedFilter"] = SavedFilterQueries.NotFound;
            }
        }

        if (errors.Count > 0)
        {
            return new GetIdeasResult(null, errors);
        }

        IReadOnlyList<Idea> recent = await ideas.GetRecentAsync(time.GetUtcNow() - IdeaQueries.ClosedShown, null, cancellationToken);
        // The non-crypto pairs of the scanner; a pair it never kept is crypto.
        IReadOnlySet<Instrument> tradFi = request.HideTradFi ? (await pairs.GetTradFiAsync(cancellationToken)).ToHashSet() : new HashSet<Instrument>();
        IEnumerable<Idea> matching = recent.Where(idea =>
            (request.Setup is null || idea.Setup == setup)
            && (request.Interval is null || idea.Interval == interval)
            && (request.Type is null || idea.Track.Plan.Type == type)
            && (request.Side is null || idea.Track.Plan.Side == side)
            && (request.Category is null || idea.Instrument.Category == category)
            && (!request.ActiveOnly || idea.IsOpen)
            && !tradFi.Contains(idea.Instrument));
        IEnumerable<IdeaView> views = IdeaView.Of(matching, await buckets.GetAsync(version.Number, cancellationToken), await buckets.GetCurvesAsync(version.Number, cancellationToken), backtests.Policy().DefaultFees);

        if (request.NonNegativeExpectancy)
        {
            views = views.Where(view => view.Bucket?.Expectancy >= 0);
        }

        if (filter is not null)
        {
            views = await MeetingAsync(views, filter, cancellationToken);
        }

        return new GetIdeasResult(page.Slice(IdeaOrder.Sort(views, column, direction)), errors);
    }

    // The snapshot is read only for a condition on a metric or a setup of the market.
    private async Task<IEnumerable<IdeaView>> MeetingAsync(IEnumerable<IdeaView> views, SavedFilter filter, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Instrument, MarketSnapshotRow> rows = new Dictionary<Instrument, MarketSnapshotRow>();
        IReadOnlyDictionary<Instrument, IReadOnlyList<PairSetup>> setups = new Dictionary<Instrument, IReadOnlyList<PairSetup>>();

        if (filter.Conditions.Any(condition => !SavedFilterFields.Of(condition.Field).OfIdea))
        {
            rows = (await snapshots.GetAllAsync(cancellationToken)).ToDictionary(row => row.Instrument);
            setups = await snapshots.GetSetupsAsync(cancellationToken);
        }

        return views.Where(view => SavedFilterMatch.Idea(
            filter.Conditions,
            view,
            rows.GetValueOrDefault(view.Idea.Instrument),
            setups.GetValueOrDefault(view.Idea.Instrument) ?? []));
    }
}