using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using ProfitFox.Domain.Entities.SavedFilters.Enums;
using ProfitFox.Domain.ValueObjects.SavedFilters;

namespace ProfitFox.Application.SavedFilters;

/// <summary>
///     The conditions of a saved filter as JSON, the stored <c>jsonb</c> value and the shape of the API alike:
///     <c>[{ "field": "change1h", "operator": "&gt;", "value": "3", "to": null }]</c>, numbers as decimal strings.
/// </summary>
public static class SavedFilterJson
{
    public static string Write(IReadOnlyList<FilterCondition> conditions)
    {
        var array = new JsonArray(
        [
            .. conditions.Select(condition => new JsonObject
            {
                ["field"] = SavedFilterFields.Of(condition.Field).Code,
                ["operator"] = condition.Operator.ToCode(),
                ["value"] = condition.Choice ?? Text(condition.Value),
                ["to"] = Text(condition.To)
            })
        ]);

        return array.ToJsonString();
    }

    /// <summary>Reads conditions this codec wrote.</summary>
    public static IReadOnlyList<FilterCondition> Read(string json)
    {
        using var document = JsonDocument.Parse(json);

        return
        [
            .. document.RootElement.EnumerateArray().Select(element =>
            {
                SavedFilterFields.TryParseOperator(element.GetProperty("operator").GetString()!, out FilterOperator op);
                SavedFilterField field = SavedFilterFields.Find(element.GetProperty("field").GetString()!)!;

                return field.Choices is null
                    ? new FilterCondition(field.Field, op, Number(element.GetProperty("value")), Number(element.GetProperty("to")))
                    : new FilterCondition(field.Field, op, Choice: element.GetProperty("value").GetString());
            })
        ];
    }

    private static string? Text(decimal? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture);
    }

    private static decimal? Number(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.Null ? null : decimal.Parse(value.GetString()!, CultureInfo.InvariantCulture);
    }
}