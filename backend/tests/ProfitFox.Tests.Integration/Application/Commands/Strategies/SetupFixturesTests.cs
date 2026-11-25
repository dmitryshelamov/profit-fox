using System.Globalization;
using System.Text.Json;

using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Execution;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Tests.Integration.MarketData;

namespace ProfitFox.Tests.Integration.Application.Commands.Strategies;

/// <summary>
///     Change add-setup-strategies, task 4.1: the helper of <c>Fixtures/Setups</c> saves a signal and a strategy by the
///     commands of the application and runs the saved strategy; the strategy H0001 written so gives the orders of the
///     reference H0001 of <c>EngineVersionTests</c> (<c>Fixtures/H0001.strategy.json</c>).
/// </summary>
[NonParallelizable]
public sealed class SetupFixturesTests
{
    [SetUp]
    public Task Reset()
    {
        return BenchDatabase.Reset();
    }

    [Test]
    public async Task The_strategy_H0001_written_as_a_setup_gives_the_orders_of_the_reference()
    {
        SavedSetup saved = await SetupFixtures.SaveAsync(
            BenchServices.Sender,
            BenchServices.Get<ISignalRepository>(),
            "funding-short",
            Path.Combine(SetupFixtures.Folder, "H0001"));
        SetupTape tape = new SetupTape(TimeSpan.FromHours(4)).Steady(6 * 28, 100, 1);

        List<StrategyStep> orders = saved.Orders(tape.Candles, Funding);
        List<StrategyStep> reference = Reference(tape.Candles);

        Assert.That((saved.Strategy.Name, saved.Strategy.Version, saved.Strategy.Kind), Is.EqualTo(("funding-short", 1, StrategyKind.Idea)));
        Assert.That(orders, Has.Count.EqualTo(28), "once a day of the four weeks, as the fake service crosses the threshold");
        Assert.That(
            orders.Select(order => (order.Moment, order.Entry, order.After, order.Size)),
            Is.EqualTo(reference.Select(order => (order.Moment, order.Entry, order.After, order.Size))));
    }

    [Test]
    public void The_placeholders_of_a_strategy_name_its_signals()
    {
        Assert.That(SetupFixtures.SignalsOf(SetupFixtures.Body("breakout.strategy.json")), Is.EqualTo(new[] { "breakout-up", "breakout-down" }));
        Assert.That(SetupFixtures.SignalsOf(SetupFixtures.Body("funding-short.strategy.json", Path.Combine(SetupFixtures.Folder, "H0001"))), Is.EqualTo(new[] { "funding-high" }));
    }

    // The payments of the fake market-data service: one every 8 hours, every third above the threshold of H0001.
    private static FundingPayment? Funding(ClosedCandle candle)
    {
        long payments = (long)((candle.CloseTime - FakeMarketData.CoveredFrom).TotalHours / 8);
        DateTimeOffset time = FakeMarketData.CoveredFrom.AddHours(8 * payments);
        return new FundingPayment(time, decimal.Parse(FakeMarketData.RateAt(time), CultureInfo.InvariantCulture));
    }

    private static List<StrategyStep> Reference(IEnumerable<ClosedCandle> candles)
    {
        string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "H0001.strategy.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        ReadResult<TradingStrategy> read = StrategyGraph.ReadStrategy(document.RootElement);
        Assert.That(read.Errors, Is.Empty);
        var executor = new StrategyExecutor(read.Value!);
        return [.. candles.Select(candle => executor.Step("BTCUSDT", candle, Funding(candle))).Where(step => step.Entry is not null)];
    }
}
