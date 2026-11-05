using ProfitFox.Domain.Entities.SavedFilters.Enums;

namespace ProfitFox.Application.SavedFilters;

/// <summary>
///     The one catalog of the fields of saved filters (decision 8): the conditions are checked by it, and the frontend takes it from the API.
///     The codes of the metrics are the names of the snapshot row in the API; the market setup holds when the pair has it on any interval;
///     the shares of an idea are fractions, and «выше базовой» is an operator of its probability.
/// </summary>
public static class SavedFilterFields
{
    private static readonly FilterOperator[] Comparisons =
    [
        FilterOperator.Greater, FilterOperator.GreaterOrEqual, FilterOperator.Less, FilterOperator.LessOrEqual, FilterOperator.Equal, FilterOperator.Between
    ];

    private static readonly string[] Setups = ["impulse", "early_impulse", "drop", "early_drop", "high_volatility"];

    /// <summary>The fields in the order of the board «Фильтр — редактор»: the market first, then the idea.</summary>
    public static readonly IReadOnlyList<SavedFilterField> All =
    [
        Choice(FilterField.Category, "category", false, ["spot", "linear"]),
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
        Choice(FilterField.Setup, "setup", false, Setups),
        Choice(FilterField.IdeaSetup, "ideaSetup", true, Setups),
        Choice(FilterField.IdeaType, "type", true, ["levels", "target_2r", "beyond_extreme", "pullback"]),
        Choice(FilterField.IdeaInterval, "interval", true, ["5", "15", "60", "240"]),
        Choice(FilterField.IdeaSide, "side", true, ["long", "short"]),
        Idea(FilterField.RiskReward, "riskReward", "ratio"),
        new SavedFilterField(FilterField.Probability, "probability", "ratio", true, [.. Comparisons, FilterOperator.AboveBaseline]),
        Idea(FilterField.Expectancy, "expectancy", "r")
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

    // A field of choices takes «=» with one of its codes.
    private static SavedFilterField Choice(FilterField field, string code, bool ofIdea, IReadOnlyList<string> choices)
    {
        return new SavedFilterField(field, code, "choice", ofIdea, [FilterOperator.Equal], choices);
    }
}