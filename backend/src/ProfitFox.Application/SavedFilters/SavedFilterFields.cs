using ProfitFox.Domain.Entities.SavedFilters.Enums;

namespace ProfitFox.Application.SavedFilters;

/// <summary>
///     The one catalog of the fields of saved filters (decision 8): the conditions are checked by it, and the frontend takes it from the API.
///     The codes of the market fields are the names of the snapshot row in the API; the shares of an idea are fractions.
/// </summary>
public static class SavedFilterFields
{
    private static readonly FilterOperator[] Comparisons =
    [
        FilterOperator.Greater, FilterOperator.GreaterOrEqual, FilterOperator.Less, FilterOperator.LessOrEqual, FilterOperator.Equal, FilterOperator.Between
    ];

    public static readonly IReadOnlyList<SavedFilterField> All =
    [
        Market(FilterField.Price, "price", "usdt"),
        Market(FilterField.Change5m, "change5m", "percent"),
        Market(FilterField.Change15m, "change15m", "percent"),
        Market(FilterField.Change1h, "change1h", "percent"),
        Market(FilterField.Change4h, "change4h", "percent"),
        Market(FilterField.Change24h, "change24h", "percent"),
        Market(FilterField.Turnover1h, "turnover1h", "usdt"),
        Market(FilterField.Turnover24h, "turnover24h", "usdt"),
        Market(FilterField.OpenInterest, "openInterest", "usdt"),
        Market(FilterField.OpenInterestChange1h, "openInterestChange1h", "percent"),
        Market(FilterField.OpenInterestChange24h, "openInterestChange24h", "percent"),
        Market(FilterField.FundingRate, "fundingRate", "ratio"),
        Market(FilterField.Spread, "spread", "percent"),
        Idea(FilterField.RiskReward, "riskReward", "ratio"),
        Idea(FilterField.Probability, "probability", "ratio"),
        Idea(FilterField.Expectancy, "expectancy", "r"),
        Idea(FilterField.Cases, "cases", "count"),
        new SavedFilterField(FilterField.AboveBaseline, "aboveBaseline", "flag", true, [FilterOperator.AboveBaseline])
    ];

    private static readonly Dictionary<string, FilterOperator> OperatorsByCode = new Dictionary<string, FilterOperator>
    {
        [">"] = FilterOperator.Greater,
        [">="] = FilterOperator.GreaterOrEqual,
        ["<"] = FilterOperator.Less,
        ["<="] = FilterOperator.LessOrEqual,
        ["="] = FilterOperator.Equal,
        ["between"] = FilterOperator.Between,
        ["above_baseline"] = FilterOperator.AboveBaseline
    };

    private static readonly Dictionary<string, SavedFilterPlace> PlacesByCode = new Dictionary<string, SavedFilterPlace>
    {
        ["market"] = SavedFilterPlace.Market,
        ["ideas"] = SavedFilterPlace.Ideas,
        ["both"] = SavedFilterPlace.Both
    };

    public static SavedFilterField? Find(string code)
    {
        return All.FirstOrDefault(field => field.Code == code);
    }

    public static SavedFilterField Of(FilterField field)
    {
        return All.Single(entry => entry.Field == field);
    }

    public static bool TryParseOperator(string code, out FilterOperator op)
    {
        return OperatorsByCode.TryGetValue(code, out op);
    }

    public static bool TryParsePlace(string code, out SavedFilterPlace place)
    {
        return PlacesByCode.TryGetValue(code, out place);
    }

    public static string ToCode(this FilterOperator op)
    {
        return OperatorsByCode.Single(pair => pair.Value == op).Key;
    }

    public static string ToCode(this SavedFilterPlace place)
    {
        return PlacesByCode.Single(pair => pair.Value == place).Key;
    }

    private static SavedFilterField Market(FilterField field, string code, string unit)
    {
        return new SavedFilterField(field, code, unit, false, Comparisons);
    }

    private static SavedFilterField Idea(FilterField field, string code, string unit)
    {
        return new SavedFilterField(field, code, unit, true, Comparisons);
    }
}