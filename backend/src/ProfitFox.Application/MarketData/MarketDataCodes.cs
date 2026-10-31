using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Application.MarketData;

/// <summary>Codes of market categories and intervals in the API and in stored rows: <c>spot</c>, <c>linear</c>, <c>1</c> … <c>720</c>, <c>D</c>, <c>W</c>, <c>M</c>.</summary>
public static class MarketDataCodes
{
    private static readonly Dictionary<string, MarketCategory> CategoriesByCode = new Dictionary<string, MarketCategory>
    {
        ["spot"] = MarketCategory.Spot,
        ["linear"] = MarketCategory.Linear
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

    public static bool TryParseCategory(string code, out MarketCategory category)
    {
        return CategoriesByCode.TryGetValue(code, out category);
    }

    public static bool TryParseInterval(string code, out Interval interval)
    {
        return IntervalsByCode.TryGetValue(code, out interval);
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