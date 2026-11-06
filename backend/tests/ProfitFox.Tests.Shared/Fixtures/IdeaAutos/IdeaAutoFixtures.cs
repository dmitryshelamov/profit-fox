using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Tests.Shared.Fixtures.Ideas;

namespace ProfitFox.Tests.Shared.Fixtures.IdeaAutos;

/// <summary>
///     Reference minutes of the paper trades of the autos, 2026-11-07 from 13:40, whose first row is the minute the idea was born in; the tests
///     write out the precomputed answers. A long enters at 100 with the stop at 98 and the target at 104 (a short at 100, 102 and 96); the
///     pullback sets wait for a limit at 100 and the market sets enter at the close of the first minute, 100.
/// </summary>
public static class IdeaAutoFixtures
{
    public static IReadOnlyList<IdeaCandle> Minutes(string name)
    {
        using Stream stream = typeof(IdeaAutoFixtures).Assembly.GetManifestResourceStream($"ProfitFox.Tests.Shared.Fixtures.IdeaAutos.{name}.json")!;
        using var document = System.Text.Json.JsonDocument.Parse(stream);

        return [.. document.RootElement.EnumerateArray().Select(IdeaFixtures.Candle)];
    }
}