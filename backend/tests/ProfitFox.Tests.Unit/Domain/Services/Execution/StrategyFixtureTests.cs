using System.Text.Json.Nodes;

using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;

namespace ProfitFox.Tests.Unit.Domain.Services.Execution;

/// <summary>
///     Change add-strategy-backtest, task 5.2 (design.md, decision 7, step 2): the strategies of H0001 and H0002 kept as
///     test files are categories of ideas, and H0002 is H0001 with a time of two days. That the files are byte for byte the
///     strategy the signal of H0001 gave with a short of 4h and of 2d was checked while that assembly was in the code.
/// </summary>
public sealed class StrategyFixtureTests
{
    [TestCase("H0001.strategy.json")]
    [TestCase("H0002.strategy.json")]
    public void The_file_is_a_category_of_ideas(string file)
    {
        Assert.That(StrategyGraph.ReadStrategy(Read(file), StrategyKind.Idea).Errors, Is.Empty);
    }

    [Test]
    public void H0002_is_H0001_with_a_time_of_two_days()
    {
        JsonObject h0001 = JsonNode.Parse(Read("H0001.strategy.json"))!.AsObject();
        JsonObject h0002 = JsonNode.Parse(Read("H0002.strategy.json"))!.AsObject();

        h0001["nodes"]!.AsArray().Single(node => (string)node!["id"]! == "close-after")!["inputs"]!["after"] = "2d";

        Assert.That(JsonNode.DeepEquals(h0001, h0002), Is.True);
    }

    private static string Read(string file)
    {
        return File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", file));
    }
}