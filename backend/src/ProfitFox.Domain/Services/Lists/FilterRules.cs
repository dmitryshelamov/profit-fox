using System.Globalization;

using ProfitFox.Domain.ValueObjects.Lists;
using ProfitFox.Domain.ValueObjects.Lists.Enums;

namespace ProfitFox.Domain.Services.Lists;

/// <summary>
///     The rules of a saved filter (change add-ideas-and-market, spec saved-filters «Сохранённый фильтр», «Условие фильтра»):
///     a name, a place and at least one condition, every condition of a field of <see cref="ListFields.Filter" /> with an
///     operator its type allows and a value it takes. Every problem is told at once, each in its place.
/// </summary>
public static class FilterRules
{
    public const int MaxName = 40;

    private static readonly string[] NumberOperators = ["gt", "ge", "lt", "le", "eq", "between"];

    public static FilterPlace? Place(string? place)
    {
        return place switch
        {
            "market" => FilterPlace.Market,
            "ideas" => FilterPlace.Ideas,
            "both" => FilterPlace.Both,
            _ => null
        };
    }

    public static string Name(FilterPlace place)
    {
        return place switch
        {
            FilterPlace.Market => "market",
            FilterPlace.Ideas => "ideas",
            _ => "both"
        };
    }

    /// <summary>
    ///     The problems of a whole filter.
    /// </summary>
    public static List<FilterProblem> Check(string? name, string? place, IReadOnlyList<FilterCondition>? conditions)
    {
        List<FilterProblem> problems = [];
        string trimmed = name?.Trim() ?? "";
        if (trimmed.Length is < 1 or > MaxName)
        {
            problems.Add(new FilterProblem("InvalidName", $"название — от 1 до {MaxName} знаков") { Field = "name" });
        }

        if (Place(place) is null)
        {
            problems.Add(new FilterProblem("InvalidPlace", "место — market, ideas или both") { Field = "place" });
        }

        problems.AddRange(CheckConditions(conditions));
        return problems;
    }

    /// <summary>
    ///     The problems of a set of conditions: none at all, or of each condition by its number.
    /// </summary>
    public static List<FilterProblem> CheckConditions(IReadOnlyList<FilterCondition>? conditions)
    {
        List<FilterProblem> problems = [];
        if (conditions is null || conditions.Count == 0)
        {
            problems.Add(new FilterProblem("NoConditions", "у фильтра хотя бы одно условие") { Field = "conditions" });
            return problems;
        }

        for (int index = 0; index < conditions.Count; index++)
        {
            if (Problem(conditions[index]) is { } reason)
            {
                problems.Add(new FilterProblem("InvalidCondition", reason) { Field = "conditions", Condition = index });
            }
        }

        return problems;
    }

    /// <summary>
    ///     The field of a condition that passed <see cref="CheckConditions" />.
    /// </summary>
    public static ListField Field(FilterCondition condition)
    {
        return ListFields.Filter.First(field => field.Name == condition.Field);
    }

    /// <summary>
    ///     A number written in the unit of its field, as the store keeps it: 0.03 % is 0.0003, 100 million is 100 000 000.
    /// </summary>
    public static decimal Stored(ListField field, string text)
    {
        return Number(text)!.Value * field.Scale;
    }

    // Why a condition is wrong, or null.
    private static string? Problem(FilterCondition condition)
    {
        if (ListFields.Filter.FirstOrDefault(field => field.Name == condition.Field) is not { } field)
        {
            return $"поля {condition.Field} нет в перечне полей";
        }

        if (field.Type == FieldType.Number)
        {
            if (!NumberOperators.Contains(condition.Operator, StringComparer.Ordinal))
            {
                return $"оператор {condition.Operator} не для числового поля {field.Title}";
            }

            if (Number(condition.Value) is null || (condition.Operator == "between" && Number(condition.To) is null))
            {
                return $"значение поля {field.Title} — число";
            }

            // The score is a number with at most two digits after the point (change decimal-label-weights, spec saved-filters
            // «Поля фильтра»).
            if (field.Name == ListFields.Score && (!Hundredths(condition.Value) || (condition.Operator == "between" && !Hundredths(condition.To))))
            {
                return $"значение поля {field.Title} — число, не больше двух знаков после точки";
            }

            // A value that does not fit once in the unit of the store (task 18.17): 7.9e22 million is more than a decimal holds.
            if (!Fits(field, condition.Value) || (condition.Operator == "between" && !Fits(field, condition.To)))
            {
                return $"значение поля {field.Title} слишком велико";
            }

            return null;
        }

        if (condition.Operator != "eq")
        {
            return $"у поля {field.Title} оператор только «равно»";
        }

        if (string.IsNullOrWhiteSpace(condition.Value))
        {
            return $"у поля {field.Title} нужно значение";
        }

        return field.Values is { } values && !values.Contains(condition.Value, StringComparer.Ordinal)
            ? $"значение поля {field.Title} — одно из {string.Join(", ", values)}"
            : null;
    }

    private static bool Hundredths(string? text)
    {
        return Number(text) is { } value && value == decimal.Round(value, 2);
    }

    private static bool Fits(ListField field, string? text)
    {
        try
        {
            _ = Number(text)!.Value * field.Scale;
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    // A number as written: a point or a comma before the fraction.
    private static decimal? Number(string? text)
    {
        return decimal.TryParse(text?.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value) ? value : null;
    }
}
