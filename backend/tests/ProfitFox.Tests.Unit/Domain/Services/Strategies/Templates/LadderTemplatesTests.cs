using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.Services.Strategies.Templates;
using ProfitFox.Domain.ValueObjects.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies.Templates;

/// <summary>Requirement «Эталонное поведение шаблонов» for the ladders.</summary>
public class LadderTemplatesTests
{
    // Scenario "Ladder": the defaults give the commands of the reference ladder.
    [Test]
    public void Ladder_places_the_sell_and_the_buy_limits_after_its_start_order_is_filled()
    {
        StrategyRun run = TemplateRuns.Start(StrategyTemplateKind.Ladder);

        run.Handle(new RunStarted(TemplateRuns.At(0))).ShouldBe([new PlaceOrder("o-1", "entry", OrderSide.Buy, OrderType.Market, null, null, 100m)]);
        run.Fill(0, "o-1", 1m, 100m).ShouldBe(
        [
            new PlaceOrder("o-2", "buy", OrderSide.Buy, OrderType.Limit, 99m, 1.0101010101010101010101010101m, null),
            new PlaceOrder("o-3", "sell", OrderSide.Sell, OrderType.Limit, 101m, 1m, null)
        ]);
    }

    // Scenario "Ladder SMA".
    [Test]
    public void Ladder_sma_buys_below_the_average_of_the_closes_not_below_the_last_entry()
    {
        StrategyRun run = TemplateRuns.Start(StrategyTemplateKind.LadderSma, ("smaPeriod", 1m));
        run.Handle(new RunStarted(TemplateRuns.At(0)));
        run.Fill(0, "o-1", 1m, 150m);

        PlaceOrder buy = run.Bar(1, 200m).OfType<PlaceOrder>().Single(order => order.NodePath == "buy");

        (buy.Side, buy.Type, buy.Price).ShouldBe((OrderSide.Buy, OrderType.Limit, 198m));
    }

    // Scenario "Ladder+ стоп".
    [Test]
    public void Ladder_plus_cancels_its_limits_and_sells_the_whole_position_once_they_are_cancelled_when_the_stop_is_hit()
    {
        StrategyRun run = TemplateRuns.Start(StrategyTemplateKind.LadderPlus);
        run.Handle(new RunStarted(TemplateRuns.At(0)));
        run.Fill(0, "o-1", 1m, 100m).Select(command => command.GetType()).ShouldBe([typeof(PlaceOrder), typeof(PlaceOrder)]);

        run.Bar(1, 80m).ShouldBe([new CancelOrder("o-2"), new CancelOrder("o-3")], ignoreOrder: true);
        run.Handle(new OrderCancelled(TemplateRuns.At(1), "o-2")).ShouldBeEmpty();
        run.Handle(new OrderCancelled(TemplateRuns.At(1), "o-3")).ShouldBe([new PlaceOrder("o-4", null, OrderSide.Sell, OrderType.Market, null, 1m, null)]);
    }

    // Scenario "Ladder+ цель по прибыли".
    [Test]
    public void Ladder_plus_does_not_start_a_position_after_the_profit_target_is_reached()
    {
        StrategyRun run = ClosedAtProfit(("profitTarget", 10m));

        run.Handle(new OrderCancelled(TemplateRuns.At(1), "o-2")).ShouldBeEmpty();
    }

    [Test]
    public void Ladder_plus_starts_the_next_position_while_the_profit_target_is_not_reached()
    {
        StrategyRun run = ClosedAtProfit(("profitTarget", 1000m));

        run.Handle(new OrderCancelled(TemplateRuns.At(1), "o-2")).ShouldBe([new PlaceOrder("o-4", "entry", OrderSide.Buy, OrderType.Market, null, null, 100m)]);
    }

    /// <summary>Ladder+ whose sell at 120 filled: the position made 20 and waits for its buy limit to be cancelled.</summary>
    private static StrategyRun ClosedAtProfit(params (string Name, decimal Value)[] values)
    {
        StrategyRun run = TemplateRuns.Start(StrategyTemplateKind.LadderPlus, [.. values, ("priceRiseForSell", 0.2m)]);
        run.Handle(new RunStarted(TemplateRuns.At(0)));
        run.Fill(0, "o-1", 1m, 100m);
        run.Fill(1, "o-3", 1m, 120m).ShouldBe([new CancelOrder("o-2")]);

        return run;
    }
}