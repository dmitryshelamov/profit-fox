using System.Text.Json;

namespace ProfitFox.Cli.Api;

/// <summary>
///     Reads why the bench API refused a request from the three shapes of its answers: a conflict (<c>reason</c>), a list of
///     errors (<c>errors[].reason</c>) and a problem (<c>detail</c>).
/// </summary>
internal static class Refusals
{
    public static async Task<string> Describe(HttpResponseMessage response, CancellationToken cancellation)
    {
        string body = await response.Content.ReadAsStringAsync(cancellation);
        List<string> reasons = [];
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("reason", out JsonElement reason) && reason.ValueKind == JsonValueKind.String)
                {
                    reasons.Add(reason.GetString()!);
                    if (root.TryGetProperty("runId", out JsonElement runId) && runId.ValueKind == JsonValueKind.String)
                    {
                        reasons.Add($"run {runId.GetString()}");
                    }
                }
                else if (root.TryGetProperty("errors", out JsonElement errors) && errors.ValueKind == JsonValueKind.Array)
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
