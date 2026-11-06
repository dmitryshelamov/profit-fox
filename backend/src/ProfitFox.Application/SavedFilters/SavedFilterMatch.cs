using ProfitFox.Application.Ideas;
using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.SavedFilters.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.SavedFilters;

namespace ProfitFox.Application.SavedFilters;

/// <summary>
///     Applies the conditions of a saved filter, joined by «и». A metric without a value does not pass, the setup of a pair holds on any
///     interval, a field of choices is «=» its code; the probability counts only for a bucket of enough cases, and «надёжно > 0» holds when the
///     badge of the bucket is ▲.
/// </summary>
public static class SavedFilterMatch
{
    /// <summary>A pair of the market meets the filter; the conditions on idea fields act only on «Идеи» and are skipped here.</summary>
    public static bool Pair(IReadOnlyList<FilterCondition> conditions, MarketSnapshotRow row, IReadOnlyList<PairSetup> setups)
    {
        return conditions.All(condition => SavedFilterFields.Of(condition.Field).OfIdea || PairHolds(condition, row, setups));
    }

    /// <summary>An idea meets the filter: its own fields from the idea and its bucket, market fields from <paramref name="row" />, the row of its pair, which an idea without one cannot meet.</summary>
    public static bool Idea(IReadOnlyList<FilterCondition> conditions, IdeaView view, MarketSnapshotRow? row, IReadOnlyList<PairSetup> setups)
    {
        return conditions.All(condition => IdeaHolds(condition, view) ?? (row is not null && PairHolds(condition, row, setups)));
    }

    private static bool? IdeaHolds(FilterCondition condition, IdeaView view)
    {
        return condition.Field switch
        {
            FilterField.IdeaSetup => view.Idea.Setup.ToCode() == condition.Choice,
            FilterField.IdeaType => view.Idea.Track.Plan.Type.ToCode() == condition.Choice,
            FilterField.IdeaInterval => view.Idea.Interval.ToCode() == condition.Choice,
            FilterField.IdeaSide => view.Idea.Track.Plan.Side.ToCode() == condition.Choice,
            FilterField.RiskReward => Holds(condition, view.Shares.NetRiskReward),
            FilterField.Probability => Holds(condition, view.Probability),
            FilterField.Expectancy when condition.Operator == FilterOperator.ReliablyPositive => view.Bucket?.Badge == IdeaBadge.Up,
            FilterField.Expectancy => Holds(condition, view.Bucket?.Expectancy),
            _ => null
        };
    }

    private static bool PairHolds(FilterCondition condition, MarketSnapshotRow row, IReadOnlyList<PairSetup> setups)
    {
        PairMetrics metrics = row.Metrics;

        return condition.Field switch
        {
            FilterField.Category => row.Instrument.Category.ToCode() == condition.Choice,
            FilterField.Setup => setups.Any(setup => setup.Setup.ToCode() == condition.Choice),
            FilterField.Change5m => Holds(condition, metrics.Change5m),
            FilterField.Change15m => Holds(condition, metrics.Change15m),
            FilterField.Change1h => Holds(condition, metrics.Change1h),
            FilterField.Change4h => Holds(condition, metrics.Change4h),
            FilterField.Change24h => Holds(condition, metrics.Change24h),
            FilterField.Turnover1h => Holds(condition, metrics.Turnover1h),
            FilterField.Turnover24h => Holds(condition, metrics.Turnover24h),
            FilterField.OpenInterest => Holds(condition, metrics.OpenInterest),
            FilterField.OpenInterestChange1h => Holds(condition, metrics.OpenInterestChange1h),
            FilterField.OpenInterestChange24h => Holds(condition, metrics.OpenInterestChange24h),
            FilterField.FundingRate => Holds(condition, metrics.FundingRate),
            _ => Holds(condition, metrics.Spread)
        };
    }

    // A comparison of a saved filter always has its value, «между» also its upper bound: the rules of saving keep no other.
    private static bool Holds(FilterCondition condition, decimal? actual)
    {
        if (actual is null)
        {
            return false;
        }

        return condition.Operator switch
        {
            FilterOperator.Greater => actual > condition.Value,
            FilterOperator.GreaterOrEqual => actual >= condition.Value,
            FilterOperator.Less => actual < condition.Value,
            FilterOperator.LessOrEqual => actual <= condition.Value,
            FilterOperator.Equal => actual == condition.Value,
            _ => actual >= condition.Value && actual <= condition.To
        };
    }
}