using System.Text.Json;

namespace ProfitFox.Tests.Shared.Fixtures.Ideas;

/// <summary>
///     Every version of the rules of ideas with the fingerprint of the reference cases it gives, oldest first. A change that changes the
///     cases adds an entry with the next version and raises <c>IdeasEngineVersion.Current</c> to it.
/// </summary>
public static class IdeasEngineVersionFixtures
{
    public static IReadOnlyList<(int Version, string Fingerprint)> History()
    {
        using Stream stream = typeof(IdeasEngineVersionFixtures).Assembly.GetManifestResourceStream("ProfitFox.Tests.Shared.Fixtures.Ideas.engine-versions.json")!;
        using var document = JsonDocument.Parse(stream);

        return [.. document.RootElement.EnumerateArray().Select(entry => (entry.GetProperty("version").GetInt32(), entry.GetProperty("fingerprint").GetString()!))];
    }
}