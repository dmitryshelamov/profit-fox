using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.BuiltIns;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.Services.Strategies.Templates;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies.Templates;

/// <summary>Requirement «Эталонное поведение шаблонов» for the trade by levels, long and short.</summary>
public class LevelTradeTemplatesTests
{
    private static PlaceOrder Limit(string id, string node, OrderSide side, decimal price, decimal quantity)
    {
        return new PlaceOrder(id, node, side, OrderType.Limit, price, quantity, null);
    }

    // Scenario "Сделка по уровням".
    [Test]
    public void Level_trade_holds_the_target_after_the_entry_fills_and_places_nothing_after_the_position_closes()
    {
        StrategyRun run = TemplateRuns.Start(StrategyTemplateKind.LevelTrade);

        run.Handle(new RunStarted(TemplateRuns.At(0))).ShouldBe([Limit("o-1", "entry", OrderSide.Buy, 100m, 1m)]);
        run.Bar(0, 100m).ShouldBeEmpty();
        run.Fill(1, "o-1", 1m, 100m).ShouldBe([Limit("o-2", "target", OrderSide.Sell, 110m, 1m)]);
        run.Fill(2, "o-2", 1m, 110m).ShouldBeEmpty();
        run.Bar(2, 110m).ShouldBeEmpty();
    }

    // Scenario "Стоп сделки по уровням".
    [Test]
    public void Level_trade_cancels_the_target_and_sells_at_the_market_when_the_close_reaches_the_stop()
    {
        StrategyRun run = EnteredLongTrade();

        run.Bar(2, 95m).ShouldBe([new CancelOrder("o-2")]);
        run.Handle(new OrderCancelled(TemplateRuns.At(2), "o-2")).ShouldBe([new PlaceOrder("o-3", null, OrderSide.Sell, OrderType.Market, null, 1m, null)]);
    }

    // Scenario "Новый запуск после сделки".
    [Test]
    public void Level_trade_restarted_after_a_closed_position_places_no_entry()
    {
        StrategyRun run = Restore(new RunSnapshot(2, 0m, 0m, 0m, PastPnl: 10m, 0m, 0m, null, null, false, 0, [], 2));

        run.Handle(new RunStarted(TemplateRuns.At(5))).ShouldBeEmpty();

        (run.State.Position, run.ValueOf("position", "closedPositions")).ShouldBe((2, new NumberValue(1m)));
    }

    // Scenario "Новый запуск без сделки".
    [Test]
    public void Level_trade_restarted_before_the_entry_filled_holds_the_entry_again()
    {
        StrategyRun run = Restore(new RunSnapshot(1, 0m, 0m, 0m, 0m, 0m, 0m, null, null, false, 0, [], 1));

        run.Handle(new RunStarted(TemplateRuns.At(5))).ShouldBe([Limit("o-2", "entry", OrderSide.Buy, 100m, 1m)]);
    }

    // Scenario "Сделка по уровням, шорт".
    [Test]
    public void Level_trade_short_sells_at_the_entry_holds_the_buy_of_the_target_and_buys_at_the_market_on_the_stop()
    {
        StrategyRun run = TemplateRuns.Start(StrategyTemplateKind.LevelTradeShort);

        run.Handle(new RunStarted(TemplateRuns.At(0))).ShouldBe([Limit("o-1", "entry", OrderSide.Sell, 100m, 1m)]);
        run.Bar(0, 100m).ShouldBeEmpty();
        run.Fill(1, "o-1", 1m, 100m).ShouldBe([Limit("o-2", "target", OrderSide.Buy, 90m, 1m)]);
        run.Bar(2, 105m).ShouldBe([new CancelOrder("o-2")]);
        run.Handle(new OrderCancelled(TemplateRuns.At(2), "o-2")).ShouldBe([new PlaceOrder("o-3", null, OrderSide.Buy, OrderType.Market, null, 1m, null)]);
    }

    /// <summary>A bar at 100, the entry bought 1 at 100 and the sell of the target stands.</summary>
    private static StrategyRun EnteredLongTrade()
    {
        StrategyRun run = TemplateRuns.Start(StrategyTemplateKind.LevelTrade);
        run.Handle(new RunStarted(TemplateRuns.At(0)));
        run.Bar(0, 100m);
        run.Fill(1, "o-1", 1m, 100m);

        return run;
    }

    private static StrategyRun Restore(RunSnapshot snapshot)
    {
        StrategyTemplate template = BuiltInTemplates.All.Single(candidate => candidate.Kind == StrategyTemplateKind.LevelTrade);

        return StrategyRun.Restore(template.Definition, BuiltInComposites.Catalog, new Dictionary<string, NodeValue>(), Conditions.Neutral, snapshot);
    }
}