using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.BuiltIns;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies.Execution;

public class TradingConditionsTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");

    // Scenario "Запуск с условиями".
    [Test]
    public void Ema_runs_with_the_balance_and_the_rules_of_the_instrument()
    {
        var conditions = new TradingConditions(1000m, new InstrumentRules(0.01m, 0.001m, 0.001m, 1m), new FeeSchedule(0.001m, 0.001m, MarketCategory.Spot));
        var run = StrategyRun.Start(
            StrategyJson.ParseStrategy(StrategyFixtures.Element("ema")),
            BuiltInComposites.Catalog,
            new Dictionary<string, NodeValue> { ["fast"] = new NumberValue(2m), ["slow"] = new NumberValue(3m) },
            conditions);
        decimal[] closes = [10m, 9m, 8m, 7m, 8m];

        IReadOnlyList<OrderCommand> commands = [.. closes.SelectMany((close, bar) => run.Handle(new BarClosed(Start.AddHours(bar), new Bar(close, close, close, close, 1m))))];
        commands.ShouldBeEmpty();
        run.Handle(new BarClosed(Start.AddHours(6), new Bar(10m, 10m, 10m, 10m, 1m)))
            .ShouldBe([new PlaceOrder("o-1", "enter", OrderSide.Buy, OrderType.Market, null, null, 100m)]);
        run.Handle(new OrderFilled(Start.AddHours(7), "o-1", 10m, 10m, IsComplete: true, Fee: 0.01m, FeeAsset: FeeAsset.Base)).ShouldBeEmpty();
    }
}