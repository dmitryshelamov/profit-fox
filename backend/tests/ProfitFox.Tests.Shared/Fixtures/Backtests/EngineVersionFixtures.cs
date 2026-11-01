using System.Text.Json;

namespace ProfitFox.Tests.Shared.Fixtures.Backtests;

/// <summary>
///     Every engine version with the fingerprint of the reference results it computes, oldest first. A change that changes the results
///     adds an entry with the next version and raises <c>EngineVersion.Current</c> to it.
/// </summary>
public static class EngineVersionFixtures
{
    public static IReadOnlyList<(int Version, string Fingerprint)> History()
    {
        using Stream stream = typeof(EngineVersionFixtures).Assembly.GetManifestResourceStream("ProfitFox.Tests.Shared.Fixtures.Backtests.engine-versions.json")!;
        using var document = JsonDocument.Parse(stream);

        return [.. document.RootElement.EnumerateArray().Select(entry => (entry.GetProperty("version").GetInt32(), entry.GetProperty("fingerprint").GetString()!))];
    }
}