using System.Globalization;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.SavedFilters.Enums;
using ProfitFox.Domain.ValueObjects.SavedFilters;

namespace ProfitFox.Application.SavedFilters;

/// <summary>
///     Checks a saved filter by the catalog of <see cref="SavedFilterFields" />: a name, a place, at least one condition, each a known field
///     with an operator it takes, a value for a comparison and also an upper bound for «между».
/// </summary>
public static class SavedFilterRules
{
    /// <exception cref="ValidationFailedException">The filter is invalid; errors keyed by <c>name</c>, <c>place</c>, <c>conditions</c> or <c>conditions[i].field</c> and alike.</exception>
    public static (string Name, SavedFilterPlace Place, IReadOnlyList<FilterCondition> Conditions) Parse(SavedFilterInput input)
    {
        var errors = new Dictionary<string, string>();
        SavedFilterPlace place = default;

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            errors["name"] = "Name is required.";
        }

        if (!SavedFilterFields.TryParsePlace(input.Place ?? "", out place))
        {
            errors["place"] = "Place must be market, ideas or both.";
        }

        if (input.Conditions is not { Count: > 0 })
        {
            errors["conditions"] = "A filter needs at least one condition.";
        }

        IReadOnlyList<FilterCondition> conditions = ParseConditions(input.Conditions, errors);

        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors);
        }

        return (input.Name!.Trim(), place, conditions);
    }

    /// <summary>Checks the conditions of a filter by the catalog, each a known field with an operator it takes, a value for a comparison and also an upper bound for «между»; none is fine. Wrong ones go to <paramref name="errors" /> keyed by <c>conditions[i].field</c> and alike.</summary>
    public static IReadOnlyList<FilterCondition> ParseConditions(IReadOnlyList<SavedFilterConditionInput>? input, IDictionary<string, string> errors)
    {
        var conditions = new List<FilterCondition>();

        foreach ((SavedFilterConditionInput condition, int index) in (input ?? []).Select((condition, index) => (condition, index)))
        {
            string key = $"conditions[{index}]";

            if (SavedFilterFields.Find(condition.Field ?? "") is not { } field)
            {
                errors[$"{key}.field"] = "Field is not in the catalog of saved filters.";
                continue;
            }

            if (!SavedFilterFields.TryParseOperator(condition.Operator ?? "", out FilterOperator op) || !field.Operators.Contains(op))
            {
                errors[$"{key}.operator"] = $"Field {field.Code} takes {string.Join(", ", field.Operators.Select(allowed => allowed.ToCode()))}.";
                continue;
            }

            if (field.Choices is { } choices)
            {
                if (choices.Contains(condition.Value ?? ""))
                {
                    conditions.Add(new FilterCondition(field.Field, op, Choice: condition.Value));
                }
                else
                {
                    errors[$"{key}.value"] = $"Field {field.Code} takes {string.Join(", ", choices)}.";
                }

                continue;
            }

            decimal? value = Number(condition.Value);
            decimal? to = Number(condition.To);

            if (op != FilterOperator.ReliablyPositive && value is null)
            {
                errors[$"{key}.value"] = "Value must be a decimal number.";
            }
            else if (op == FilterOperator.Between && to is null)
            {
                errors[$"{key}.to"] = "Between needs an upper bound, a decimal number.";
            }
            else
            {
                conditions.Add(new FilterCondition(field.Field, op, op == FilterOperator.ReliablyPositive ? null : value, op == FilterOperator.Between ? to : null));
            }
        }

        return conditions;
    }

    private static decimal? Number(string? text)
    {
        return decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value) ? value : null;
    }
}