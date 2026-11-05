using ProfitFox.Application.Paging;

namespace ProfitFox.Application.Ideas;

/// <summary>
///     The order of the ideas of «Идеи»: by a column in either direction, ideas without a value in the column last whichever way it goes, and
///     equal values by <see cref="ByExpectancy" />, so the order of the same ideas never changes. Setup, type, side and status go in the order of
///     their lists, which is the order of the enums; the probability counts only for a bucket of enough cases; the profit goes by the profit on
///     1 USDT.
/// </summary>
public static class IdeaOrder
{
    public const string InvalidColumn =
        "Sort must be firedAt, symbol, setup, interval, type, side, entry, stop, target, riskReward, probability, expectancy, profit or status.";

    private static readonly Dictionary<string, IdeaSortColumn> ColumnsByCode = new Dictionary<string, IdeaSortColumn>
    {
        ["firedAt"] = IdeaSortColumn.FiredAt,
        ["symbol"] = IdeaSortColumn.Symbol,
        ["setup"] = IdeaSortColumn.Setup,
        ["interval"] = IdeaSortColumn.Interval,
        ["type"] = IdeaSortColumn.Type,
        ["side"] = IdeaSortColumn.Side,
        ["entry"] = IdeaSortColumn.Entry,
        ["stop"] = IdeaSortColumn.Stop,
        ["target"] = IdeaSortColumn.Target,
        ["riskReward"] = IdeaSortColumn.RiskReward,
        ["probability"] = IdeaSortColumn.Probability,
        ["expectancy"] = IdeaSortColumn.Expectancy,
        ["profit"] = IdeaSortColumn.Profit,
        ["status"] = IdeaSortColumn.Status
    };

    public static bool TryParseColumn(string code, out IdeaSortColumn column)
    {
        return ColumnsByCode.TryGetValue(code, out column);
    }

    public static IReadOnlyList<IdeaView> Sort(IEnumerable<IdeaView> views, IdeaSortColumn column, OrderDirection direction)
    {
        if (column == IdeaSortColumn.Symbol)
        {
            IOrderedEnumerable<IdeaView> bySymbol = direction == OrderDirection.Ascending
                ? views.OrderBy(view => view.Idea.Instrument.Symbol, StringComparer.Ordinal)
                : views.OrderByDescending(view => view.Idea.Instrument.Symbol, StringComparer.Ordinal);

            return [.. ThenByExpectancy(bySymbol)];
        }

        IdeaView[] all = [.. views];
        IdeaView[] with = [.. all.Where(view => Key(view, column) is not null)];
        IOrderedEnumerable<IdeaView> byKey = direction == OrderDirection.Ascending ? with.OrderBy(view => Key(view, column)) : with.OrderByDescending(view => Key(view, column));

        return [.. ThenByExpectancy(byKey), .. ByExpectancy(all.Where(view => Key(view, column) is null))];
    }

    /// <summary>Highest expectancy first, ideas of a bucket without cases last; newest first among equals, then by id.</summary>
    public static IReadOnlyList<IdeaView> ByExpectancy(IEnumerable<IdeaView> views)
    {
        return [.. ThenByExpectancy(views.OrderBy(_ => 0))];
    }

    private static IOrderedEnumerable<IdeaView> ThenByExpectancy(IOrderedEnumerable<IdeaView> views)
    {
        return views.ThenByDescending(view => view.Bucket?.Expectancy ?? decimal.MinValue).ThenByDescending(view => view.Idea.Track.FiredAt).ThenBy(view => view.Idea.Id);
    }

    private static decimal? Key(IdeaView view, IdeaSortColumn column)
    {
        return column switch
        {
            IdeaSortColumn.FiredAt => view.Idea.Track.FiredAt.UtcTicks,
            IdeaSortColumn.Setup => (int)view.Idea.Setup,
            IdeaSortColumn.Interval => (int)view.Idea.Interval,
            IdeaSortColumn.Type => (int)view.Idea.Track.Plan.Type,
            IdeaSortColumn.Side => (int)view.Idea.Track.Plan.Side,
            IdeaSortColumn.Entry => view.Idea.Track.Plan.Entry,
            IdeaSortColumn.Stop => view.Idea.Track.Plan.Stop,
            IdeaSortColumn.Target => view.Idea.Track.Plan.Target,
            IdeaSortColumn.RiskReward => view.Idea.Track.Plan.RiskReward,
            IdeaSortColumn.Probability => view.Probability,
            IdeaSortColumn.Expectancy => view.Bucket?.Expectancy,
            IdeaSortColumn.Profit => view.Shares.ProfitPerUsdt,
            _ => (int)view.Idea.Track.Status
        };
    }
}