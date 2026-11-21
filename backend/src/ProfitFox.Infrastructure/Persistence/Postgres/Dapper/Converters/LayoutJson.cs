using System.Globalization;
using System.Text;
using System.Text.Json;

using ProfitFox.Domain.ValueObjects.Signals;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Converters;

/// <summary>
///     The <c>layout</c> object of a saved signal or strategy: each node id maps to <c>{ "x": number, "y": number }</c>.
/// </summary>
internal static class LayoutJson
{
    /// <summary>
    ///     The layout in the order of the ids, so the same positions are the same text.
    /// </summary>
    public static string Write(IReadOnlyDictionary<string, NodePosition> layout)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach ((string id, NodePosition position) in layout.OrderBy(position => position.Key, StringComparer.Ordinal))
            {
                writer.WriteStartObject(id);
                writer.WriteNumber("x", position.X);
                writer.WriteNumber("y", position.Y);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    ///     The positions of a <c>layout</c> object, added to <paramref name="layout" />, and the errors of those that are not.
    /// </summary>
    public static IReadOnlyList<string> Read(JsonElement element, Dictionary<string, NodePosition> layout)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return ["layout must be an object"];
        }

        List<string> errors = [];
        foreach (JsonProperty node in element.EnumerateObject())
        {
            if (node.Value.ValueKind == JsonValueKind.Object
                && node.Value.TryGetProperty("x", out JsonElement x) && x.TryGetDouble(out double left)
                && node.Value.TryGetProperty("y", out JsonElement y) && y.TryGetDouble(out double top)
                && double.IsFinite(left) && double.IsFinite(top))
            {
                layout[node.Name] = new NodePosition(left, top);
            }
            else
            {
                errors.Add(string.Create(CultureInfo.InvariantCulture, $"layout.{node.Name} must be {{ x, y }} with numbers"));
            }
        }

        return errors;
    }
}
