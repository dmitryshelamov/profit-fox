using Dapper;

using ProfitFox.Domain.Services.Lists;
using ProfitFox.Domain.ValueObjects.Lists;
using ProfitFox.Domain.ValueObjects.Lists.Enums;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Lists;

/// <summary>
///     The columns of the lists (change add-ideas-and-market, design.md, decision 10): each field of
///     <see cref="ListFields" /> by its name, as SQL over the aliases <c>m</c> (<c>market_pairs</c>), <c>i</c> (<c>ideas</c>)
///     and <c>s</c> (<c>strategies</c>). <c>ORDER BY</c> and <c>WHERE</c> are built of these only; every value goes as a
///     parameter.
/// </summary>
internal static class ListColumns
{
    public const string OpenIdeasOfPair = "(SELECT count(*) FROM ideas o WHERE o.symbol = m.symbol AND o.state = 'open')";

    public const string StrategyName = "COALESCE(s.name, i.strategy_name)";

    // An open idea counts by its result now, a closed one by its outcome.
    public const string Result = "(CASE WHEN i.state = 'open' THEN i.now_result ELSE i.result END)";

    /// <summary>
    ///     The score of the idea under <paramref name="alias" /> (change add-idea-score, design.md, decision 2): the sum of the
    ///     weights of its labels by its side, by the weights now; 0 without labels or weights.
    /// </summary>
    public static string Score(string alias)
    {
        return $"(SELECT COALESCE(sum(CASE WHEN {alias}.side = 'long' THEN w.long_weight ELSE w.short_weight END), 0)::int FROM label_weights w WHERE w.text = ANY({alias}.labels))";
    }

    public static readonly IReadOnlyDictionary<string, string> Market = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["symbol"] = "m.symbol",
        ["price"] = "m.price",
        ["change1h"] = "m.change_1h",
        ["change4h"] = "m.change_4h",
        ["change24h"] = "m.change_24h",
        ["turnover24h"] = "m.turnover_24h",
        ["openInterest"] = "m.open_interest_usdt",
        ["fundingRate"] = "m.funding_rate",
        ["ideas"] = OpenIdeasOfPair
    };

    public static readonly IReadOnlyDictionary<string, string> Ideas = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["symbol"] = "i.symbol",
        ["strategy"] = StrategyName,
        ["timeframe"] = "(CASE i.timeframe WHEN '5m' THEN 1 WHEN '15m' THEN 2 WHEN '1h' THEN 3 WHEN '4h' THEN 4 ELSE 5 END)",
        ["side"] = "i.side",
        ["labels"] = "NULLIF(array_to_string(i.labels, ' '), '')",
        [ListFields.Score] = Score("i"),
        ["moment"] = "i.moment",
        ["entry"] = "COALESCE(i.entry_price, i.candle_close)",
        ["stop"] = "i.stop_price",
        ["target"] = "i.target_price",
        ["left"] = "(CASE WHEN i.state = 'open' THEN i.until END)",
        ["now"] = "i.now_result",
        ["exitTime"] = "i.exit_time",
        ["exitReason"] = "i.exit_reason",
        ["result"] = Result,
        ["state"] = "i.state"
    };

    public static readonly IReadOnlyDictionary<string, string> Summary = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["strategy"] = "\"Strategy\"",
        ["timeframe"] = "(CASE \"Timeframe\" WHEN '5m' THEN 1 WHEN '15m' THEN 2 WHEN '1h' THEN 3 WHEN '4h' THEN 4 ELSE 5 END)",
        ["ideas"] = "\"Ideas\"",
        ["wins"] = "\"Wins\"",
        ["mean"] = "\"Mean\"",
        ["sum"] = "\"Sum\""
    };

    // The fields of a filter: those of the market over m, those of an idea over i and s.
    private static readonly IReadOnlyDictionary<string, string> FilterColumns = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["price"] = "m.price",
        ["change1h"] = "m.change_1h",
        ["change4h"] = "m.change_4h",
        ["change24h"] = "m.change_24h",
        ["turnover24h"] = "m.turnover_24h",
        ["openInterest"] = "m.open_interest_usdt",
        ["fundingRate"] = "m.funding_rate",
        ["openIdeas"] = OpenIdeasOfPair,
        ["strategy"] = StrategyName,
        ["timeframe"] = "i.timeframe",
        ["side"] = "i.side",
        [ListFields.Score] = Score("i"),
        ["result"] = Result,
        ["minutesLeft"] = "(EXTRACT(EPOCH FROM (i.until - now())) / 60)",
        ["hasStop"] = "(i.stop IS NOT NULL)"
    };

    /// <summary>
    ///     <c>ORDER BY</c> of the column of the field in the direction, a missing value last in either, then the tie-break.
    /// </summary>
    public static string OrderBy(IReadOnlyDictionary<string, string> columns, ListOrder order, string tieBreak)
    {
        string column = columns.TryGetValue(order.Sort, out string? found) ? found : throw new ArgumentException($"There is no column {order.Sort}.", nameof(order));
        return $"ORDER BY {column} {(order.Descending ? "DESC" : "ASC")} NULLS LAST, {tieBreak}";
    }

    /// <summary>
    ///     The conditions of <paramref name="place" /> joined by «и», each of a field of the filter with its values as parameters
    ///     named after <paramref name="prefix" />; <c>TRUE</c> without any.
    /// </summary>
    public static string Conditions(IReadOnlyList<FilterCondition>? conditions, FieldPlace place, DynamicParameters parameters, string prefix)
    {
        List<string> parts = [];
        int index = 0;
        foreach (FilterCondition condition in conditions ?? [])
        {
            ListField field = FilterRules.Field(condition);
            if (field.Place == place)
            {
                parts.Add(Condition(field, condition, parameters, $"{prefix}{index}"));
            }

            index++;
        }

        return parts.Count == 0 ? "TRUE" : string.Join(" AND ", parts);
    }

    public static bool Has(IReadOnlyList<FilterCondition>? conditions, FieldPlace place)
    {
        return (conditions ?? []).Any(condition => FilterRules.Field(condition).Place == place);
    }

    private static string Condition(ListField field, FilterCondition condition, DynamicParameters parameters, string name)
    {
        if (field.Name == "label")
        {
            parameters.Add(name, condition.Value);
            return $"@{name} = ANY(i.labels)";
        }

        string column = FilterColumns[field.Name];
        if (field.Type == FieldType.Boolean)
        {
            parameters.Add(name, condition.Value == "true");
            return $"{column} = @{name}";
        }

        if (field.Type != FieldType.Number)
        {
            parameters.Add(name, condition.Value);
            return $"{column} = @{name}";
        }

        parameters.Add(name, FilterRules.Stored(field, condition.Value!));
        if (condition.Operator == "between")
        {
            parameters.Add($"{name}b", FilterRules.Stored(field, condition.To!));
            return $"{column} BETWEEN LEAST(@{name}, @{name}b) AND GREATEST(@{name}, @{name}b)";
        }

        string sign = condition.Operator switch
        {
            "gt" => ">",
            "ge" => ">=",
            "lt" => "<",
            "le" => "<=",
            _ => "="
        };
        return $"{column} {sign} @{name}";
    }
}
