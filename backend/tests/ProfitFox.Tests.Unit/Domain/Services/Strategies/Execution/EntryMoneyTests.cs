using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.BuiltIns;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.Services.Strategies.Validation;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies.Execution;

/// <summary>An entry order goes out only when free money pays for it with its fee in the quote asset; exits are never limited.</summary>
public class EntryMoneyTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");

    // Scenario "Не хватает на докупку".
    [Test]
    public void Ladder_without_money_for_the_averaging_buy_places_only_the_sell_and_checks_again_on_the_next_event()
    {
        var run = StrategyRun.Start(
            StrategyJson.ParseStrategy(StrategyFixtures.Element("ladder")),
            BuiltInComposites.Catalog,
            new Dictionary<string, NodeValue>(),
            Conditions.Neutral with { Balance = 160m, Fees = new FeeSchedule(0m, 0m, MarketCategory.Spot) });
        run.Handle(new RunStarted(Start)).ShouldHaveSingleItem();

        run.Handle(new OrderFilled(Start, "o-1", 1m, 100m, IsComplete: true))
            .ShouldBe([new PlaceOrder("o-2", "ladder/sell", OrderSide.Sell, OrderType.Limit, 101m, 1m, null)]);
        run.SkippedOrders.ShouldBe(1);

        run.Handle(new BarClosed(Start.AddHours(1), new Bar(100m, 100m, 100m, 100m, 1m))).ShouldBeEmpty();
        run.SkippedOrders.ShouldBe(2);
    }

    // Scenario "Выход без ограничения".
    [Test]
    public void Exit_goes_out_with_no_free_money()
    {
        Graph graph = new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("flat", SimpleNodeKind.Not)
            .Node("enter", SimpleNodeKind.MarketOrder, Settings(OrderSide.Buy, OrderSizeMode.Amount), ("size", Values.Number(100m)))
            .Node("exit", SimpleNodeKind.ClosePosition)
            .Wire("position.hasPosition", "flat.value")
            .Wire("flat.result", "enter.when")
            .Wire("position.hasPosition", "exit.when")
            .Build();
        StrategyRun run = Run(graph, 100m);
        run.Handle(new RunStarted(Start));

        IReadOnlyList<OrderCommand> commands = run.Handle(new OrderFilled(Start, "o-1", 1m, 100m, IsComplete: true));

        run.ValueOf("position", "free").ShouldBe(Values.Number(0m));
        commands.ShouldBe([new PlaceOrder("o-2", "exit", OrderSide.Sell, OrderType.Market, null, 1m, null)]);
        run.SkippedOrders.ShouldBe(0);
    }

    [Test]
    public void Unfilled_market_entry_keeps_its_money_from_the_next_entry()
    {
        Graph graph = new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("flat", SimpleNodeKind.Not)
            .Node("first", SimpleNodeKind.MarketOrder, Settings(OrderSide.Buy, OrderSizeMode.Amount), ("size", Values.Number(800m)))
            .Node("second", SimpleNodeKind.MarketOrder, Settings(OrderSide.Buy, OrderSizeMode.Quantity), ("size", Values.Number(3m)))
            .Wire("position.hasPosition", "flat.value")
            .Wire("flat.result", "first.when")
            .Wire("flat.result", "second.when")
            .Build();
        StrategyRun run = Run(graph, 1000m);
        run.Handle(new BarClosed(Start, new Bar(100m, 100m, 100m, 100m, 1m))).ShouldBe([new PlaceOrder("o-1", "first", OrderSide.Buy, OrderType.Market, null, null, 800m)]);
        run.SkippedOrders.ShouldBe(1);

        run.Handle(new BarClosed(Start.AddHours(1), new Bar(100m, 100m, 100m, 100m, 1m)));

        run.State.Reserved.ShouldBe(800m);
        run.Handle(new OrderFilled(Start.AddHours(1), "o-1", 8m, 100m, IsComplete: true));
        run.State.Reserved.ShouldBe(0m);
    }

    [Test]
    public void Market_entry_by_quantity_is_valued_at_the_last_close()
    {
        Graph graph = new GraphBuilder()
            .Node("enter", SimpleNodeKind.MarketOrder, Settings(OrderSide.Buy, OrderSizeMode.Quantity), ("when", Values.Boolean(true)), ("size", Values.Number(1m)))
            .Build();
        StrategyRun run = Run(graph, 150m);

        run.Handle(new BarClosed(Start, new Bar(200m, 200m, 200m, 200m, 1m))).ShouldBeEmpty();
        run.SkippedOrders.ShouldBe(1);

        run.Handle(new BarClosed(Start.AddHours(1), new Bar(149m, 149m, 149m, 149m, 1m)))
            .ShouldBe([new PlaceOrder("o-1", "enter", OrderSide.Buy, OrderType.Market, null, 1m, null)]);
    }

    [Test]
    public void Market_entry_by_quantity_before_the_first_bar_has_no_price_to_check()
    {
        Graph graph = new GraphBuilder()
            .Node("enter", SimpleNodeKind.MarketOrder, Settings(OrderSide.Buy, OrderSizeMode.Quantity), ("when", Values.Boolean(true)), ("size", Values.Number(1m)))
            .Build();

        Run(graph, 1m).Handle(new RunStarted(Start)).ShouldBe([new PlaceOrder("o-1", "enter", OrderSide.Buy, OrderType.Market, null, 1m, null)]);
    }

    [TestCase(100, 1)]
    [TestCase(100.05, 0)]
    public void Market_entry_by_amount_pays_its_fee_in_the_quote_asset(decimal balance, int skipped)
    {
        Graph graph = new GraphBuilder()
            .Node("enter", SimpleNodeKind.MarketOrder, Settings(OrderSide.Buy, OrderSizeMode.Amount), ("when", Values.Boolean(true)), ("size", Values.Number(100m)))
            .Build();
        var run = StrategyRun.Start(
            StrategyDefinitions.Long(graph),
            CompositeCatalog.Empty,
            new Dictionary<string, NodeValue>(),
            Conditions.Neutral with { Balance = balance, Fees = new FeeSchedule(0.0002m, 0.0005m, MarketCategory.Linear) });

        run.Handle(new RunStarted(Start));

        run.SkippedOrders.ShouldBe(skipped);
    }

    // Scenario "Не хватило баланса" of the results: three buys of 100 on a balance of 250.
    [Test]
    public void Limit_entry_that_does_not_fit_is_skipped()
    {
        Graph graph = new GraphBuilder()
            .Node("buy", SimpleNodeKind.HoldLimit, Settings(OrderSide.Buy, OrderSizeMode.Amount), ("when", Values.Boolean(true)), ("price", Values.Number(50m)), ("size", Values.Number(100m)))
            .Build();
        StrategyRun run = Run(graph, 250m);

        run.Handle(new RunStarted(Start)).ShouldHaveSingleItem();
        run.Handle(new OrderFilled(Start, "o-1", 2m, 50m, IsComplete: true)).ShouldHaveSingleItem();
        run.Handle(new OrderFilled(Start, "o-2", 2m, 50m, IsComplete: true)).ShouldBeEmpty();

        run.SkippedOrders.ShouldBe(1);
    }

    private static StrategyRun Run(Graph graph, decimal balance)
    {
        return StrategyRun.Start(StrategyDefinitions.Long(graph), CompositeCatalog.Empty, new Dictionary<string, NodeValue>(), Conditions.Neutral with { Balance = balance });
    }

    private static NodeSettings Settings(OrderSide side, OrderSizeMode size)
    {
        return new NodeSettings(Side: side, SizeMode: size);
    }
}