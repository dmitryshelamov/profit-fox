using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Application.MarketData;

/// <summary>
///     Codes of market categories and intervals in the API and in stored rows: <c>spot</c>, <c>linear</c>, <c>moex</c>, <c>1</c> … <c>720</c>,
///     <c>D</c>, <c>W</c>, <c>M</c>. The intervals are a property of the category: Bybit has all 13, MOEX the eight up to four hours.
/// </summary>
public static class MarketDataCodes
{
    public const string InvalidCategory = "Category must be spot, linear or moex.";

    private static readonly Dictionary<string, MarketCategory> CategoriesByCode = new Dictionary<string, MarketCategory>
    {
        ["spot"] = MarketCategory.Spot,
        ["linear"] = MarketCategory.Linear,
        ["moex"] = MarketCategory.Moex
    };

    private static readonly Dictionary<string, Interval> IntervalsByCode = new Dictionary<string, Interval>
    {
        ["1"] = Interval.OneMinute,
        ["3"] = Interval.ThreeMinutes,
        ["5"] = Interval.FiveMinutes,
        ["15"] = Interval.FifteenMinutes,
        ["30"] = Interval.ThirtyMinutes,
        ["60"] = Interval.OneHour,
        ["120"] = Interval.TwoHours,
        ["240"] = Interval.FourHours,
        ["360"] = Interval.SixHours,
        ["720"] = Interval.TwelveHours,
        ["D"] = Interval.OneDay,
        ["W"] = Interval.OneWeek,
        ["M"] = Interval.OneMonth
    };

    private static readonly Dictionary<MarketCategory, string> CodesByCategory = CategoriesByCode.ToDictionary(pair => pair.Value, pair => pair.Key);

    private static readonly Dictionary<Interval, string> CodesByInterval = IntervalsByCode.ToDictionary(pair => pair.Value, pair => pair.Key);

    // T-Invest gives no half-day candles, and a daily candle of MOEX opens off the UTC grid of the system (decision 1).
    private static readonly Interval[] MoexIntervals =
    [
        Interval.OneMinute,
        Interval.ThreeMinutes,
        Interval.FiveMinutes,
        Interval.FifteenMinutes,
        Interval.ThirtyMinutes,
        Interval.OneHour,
        Interval.TwoHours,
        Interval.FourHours
    ];

    private static readonly Interval[] BybitIntervals = Enum.GetValues<Interval>();

    public static bool TryParseCategory(string code, out MarketCategory category)
    {
        return CategoriesByCode.TryGetValue(code, out category);
    }

    public static bool TryParseInterval(string code, out Interval interval)
    {
        return IntervalsByCode.TryGetValue(code, out interval);
    }

    /// <summary>Parses an interval that is in the list of the category.</summary>
    public static bool TryParseInterval(MarketCategory category, string code, out Interval interval)
    {
        return TryParseInterval(code, out interval) && IntervalsOf(category).Contains(interval);
    }

    /// <summary>The intervals of the candles of a category, in the order of <see cref="Interval" />.</summary>
    public static IReadOnlyList<Interval> IntervalsOf(MarketCategory category)
    {
        return category == MarketCategory.Moex ? MoexIntervals : BybitIntervals;
    }

    /// <summary>The error of an interval that is not in the list of the category.</summary>
    public static string InvalidInterval(MarketCategory category)
    {
        return $"Interval must be one of the intervals of {category.ToCode()}: {string.Join(", ", IntervalsOf(category).Select(ToCode))}.";
    }

    public static string ToCode(this MarketCategory category)
    {
        return CodesByCategory[category];
    }

    public static string ToCode(this Interval interval)
    {
        return CodesByInterval[interval];
    }
}