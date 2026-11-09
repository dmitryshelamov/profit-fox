using MediatR;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ideas;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.SavedFilters;

namespace ProfitFox.Application.SavedFilters.Queries.FitSavedFilter;

public sealed class FitSavedFilterQueryHandler(
    IMarketSnapshotRepository snapshots,
    IIdeaRepository ideas,
    IIdeaBucketRepository buckets,
    IdeasEngineVersion version,
    BacktestSettings backtests,
    TimeProvider time) : IRequestHandler<FitSavedFilterQuery, FitSavedFilterResult>
{
    /// <exception cref="ValidationFailedException">A condition is invalid.</exception>
    public async Task<FitSavedFilterResult> Handle(FitSavedFilterQuery request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        IReadOnlyList<FilterCondition> conditions = SavedFilterRules.ParseConditions(request.Conditions, errors);

        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors);
        }

        IReadOnlyList<MarketSnapshotRow> rows = await snapshots.GetAllAsync(cancellationToken);
        IReadOnlyDictionary<Instrument, IReadOnlyList<PairSetup>> setups = await snapshots.GetSetupsAsync(cancellationToken);
        var rowsByPair = rows.ToDictionary(row => row.Instrument);
        IReadOnlyList<Idea> recent = await ideas.GetRecentAsync(time.GetUtcNow() - IdeaQueries.ClosedShown, null, cancellationToken);
        IReadOnlyList<IdeaView> views = IdeaView.Of(recent, await buckets.GetAsync(version.Number, cancellationToken), await buckets.GetCurvesAsync(version.Number, cancellationToken), backtests.Policy().DefaultFees);

        return new FitSavedFilterResult(
            rows.Count(row => SavedFilterMatch.Pair(conditions, row, setups.GetValueOrDefault(row.Instrument) ?? [])),
            views.Count(view => SavedFilterMatch.Idea(conditions, view, rowsByPair.GetValueOrDefault(view.Idea.Instrument), setups.GetValueOrDefault(view.Idea.Instrument) ?? [])));
    }
}