using System.Text.Json;

namespace ProfitFox.Cli.Api;

/// <summary>
///     Reads why the bench API refused a request from the shapes of its answers: a list of errors (<c>errors[].reason</c>)
///     and a problem (<c>detail</c>).
/// </summary>
internal static class Refusals
{
    public static async Task<string> Describe(HttpResponseMessage response, CancellationToken cancellation)
    {
        string body = await response.Content.ReadAsStringAsync(cancellation);
        List<string> reasons = [];
        try
        {
            using var document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("errors", out JsonElement errors) && errors.ValueKind == JsonValueKind.Array)
                {
                    reasons.AddRange(errors.EnumerateArray()
                        .Select(error => error.TryGetProperty("reason", out JsonElement text) ? text.GetString() : null)
                        .OfType<string>());
                }
                else if (root.TryGetProperty("detail", out JsonElement detail) && detail.ValueKind == JsonValueKind.String)
                {
                    reasons.Add(detail.GetString()!);
                }
            }
        }
        catch (JsonException)
        {
        }

        return reasons.Count > 0
            ? string.Join(Environment.NewLine, reasons)
            : $"{(int)response.StatusCode} {response.ReasonPhrase}";
    }
}
