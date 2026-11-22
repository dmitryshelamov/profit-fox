using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Tests.Unit.Domain.Services.Graph;

namespace ProfitFox.Tests.Unit.Domain.Services.Execution;

/// <summary>
///     Change add-strategy-backtest, task 5.2 (design.md, decision 7, step 2): the strategies of H0001 and H0002 kept as
///     test files are byte for byte what the strategy of the signal of H0001 gives with a short of 4h and of 2d, and both
///     are categories of ideas. The comparison with the strategy of the signal goes with it (task 8.3).
/// </summary>
public sealed class StrategyFixtureTests
{
    [TestCase("H0001.strategy.json", 4)]
    [TestCase("H0002.strategy.json", 48)]
    public void The_file_is_the_strategy_of_the_signal_of_H0001(string file, int hours)
    {
        Signal signal = StrategyGraph.ReadSignal(Graphs.H0001).Value!;

        string written = StrategyGraph.Write(TradingStrategy.FromSignal(signal, EntrySide.Short, TimeSpan.FromHours(hours)).Graph);

        Assert.That(Read(file), Is.EqualTo(written));
        Assert.That(StrategyGraph.ReadStrategy(Read(file), StrategyKind.Idea).Errors, Is.Empty);
    }

    private static string Read(string file)
    {
        return File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", file));
    }
}
