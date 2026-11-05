using ProfitFox.Application.MarketData;
using ProfitFox.Application.Paging;
using ProfitFox.Domain.ValueObjects.Market;

namespace ProfitFox.Application.Market;

/// <summary>
///     The order of the rows of «Рынок»: by a column in either direction, rows without a value in the column last whichever way it goes, and
///     equal values by category code and symbol, compared ordinally, so the order of the same rows never changes.
/// </summary>
public static class MarketOrder
{
    public const string InvalidColumn =
        "Sort must be symbol, price, change5m, change15m, change1h, change4h, change24h, turnover1h, turnover24h, openInterest, openInterestChange1h, openInterestChange24h, fundingRate or spread.";

    private static readonly Dictionary<string, MarketSortColumn> ColumnsByCode = new Dictionary<string, MarketSortColumn>
    {
        ["symbol"] = MarketSortColumn.Symbol,
        ["price"] = MarketSortColumn.Price,
        ["change5m"] = MarketSortColumn.Change5m,
        ["change15m"] = MarketSortColumn.Change15m,
        ["change1h"] = MarketSortColumn.Change1h,
        ["change4h"] = MarketSortColumn.Change4h,
        ["change24h"] = MarketSortColumn.Change24h,
        ["turnover1h"] = MarketSortColumn.Turnover1h,
        ["turnover24h"] = MarketSortColumn.Turnover24h,
        ["openInterest"] = MarketSortColumn.OpenInterest,
        ["openInterestChange1h"] = MarketSortColumn.OpenInterestChange1h,
        ["openInterestChange24h"] = MarketSortColumn.OpenInterestChange24h,
        ["fundingRate"] = MarketSortColumn.FundingRate,
        ["spread"] = MarketSortColumn.Spread
    };

    public static bool TryParseColumn(string code, out MarketSortColumn column)
    {
        return ColumnsByCode.TryGetValue(code, out column);
    }

    public static IReadOnlyList<MarketSnapshotRow> Sort(IEnumerable<MarketSnapshotRow> rows, MarketSortColumn column, OrderDirection direction)
    {
        if (column == MarketSortColumn.Symbol)
        {
            IOrderedEnumerable<MarketSnapshotRow> bySymbol = direction == OrderDirection.Ascending
                ? rows.OrderBy(row => row.Instrument.Symbol, StringComparer.Ordinal)
                : rows.OrderByDescending(row => row.Instrument.Symbol, StringComparer.Ordinal);

            return [.. ThenByPair(bySymbol)];
        }

        MarketSnapshotRow[] all = [.. rows];
        MarketSnapshotRow[] with = [.. all.Where(row => Metric(row.Metrics, column) is not null)];
        IOrderedEnumerable<MarketSnapshotRow> byMetric = direction == OrderDirection.Ascending
            ? with.OrderBy(row => Metric(row.Metrics, column))
            : with.OrderByDescending(row => Metric(row.Metrics, column));

        return [.. ThenByPair(byMetric), .. ByPair(all.Where(row => Metric(row.Metrics, column) is null))];
    }

    private static IOrderedEnumerable<MarketSnapshotRow> ByPair(IEnumerable<MarketSnapshotRow> rows)
    {
        return ThenByPair(rows.OrderBy(_ => 0));
    }

    private static IOrderedEnumerable<MarketSnapshotRow> ThenByPair(IOrderedEnumerable<MarketSnapshotRow> rows)
    {
        return rows.ThenBy(row => row.Instrument.Category.ToCode(), StringComparer.Ordinal).ThenBy(row => row.Instrument.Symbol, StringComparer.Ordinal);
    }

    private static decimal? Metric(PairMetrics metrics, MarketSortColumn column)
    {
        return column switch
        {
            MarketSortColumn.Price => metrics.Price,
            MarketSortColumn.Change5m => metrics.Change5m,
            MarketSortColumn.Change15m => metrics.Change15m,
            MarketSortColumn.Change1h => metrics.Change1h,
            MarketSortColumn.Change4h => metrics.Change4h,
            MarketSortColumn.Change24h => metrics.Change24h,
            MarketSortColumn.Turnover1h => metrics.Turnover1h,
            MarketSortColumn.Turnover24h => metrics.Turnover24h,
            MarketSortColumn.OpenInterest => metrics.OpenInterest,
            MarketSortColumn.OpenInterestChange1h => metrics.OpenInterestChange1h,
            MarketSortColumn.OpenInterestChange24h => metrics.OpenInterestChange24h,
            MarketSortColumn.FundingRate => metrics.FundingRate,
            _ => metrics.Spread
        };
    }
}