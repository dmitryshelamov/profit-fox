using System.Text.Json;

namespace ProfitFox.Tests.Shared.Fixtures.Strategies;

/// <summary>Reference strategies EMA, EMA Short, DCA and Ladder as API documents.</summary>
public static class StrategyFixtures
{
    public static string Json(string name)
    {
        using Stream stream = typeof(StrategyFixtures).Assembly.GetManifestResourceStream($"ProfitFox.Tests.Shared.Fixtures.Strategies.{name}.json")!;
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    public static JsonElement Element(string name)
    {
        return JsonDocument.Parse(Json(name)).RootElement;
    }
}