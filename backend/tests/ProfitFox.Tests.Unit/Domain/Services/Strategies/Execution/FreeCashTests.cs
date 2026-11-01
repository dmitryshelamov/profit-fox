using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.Services.Strategies.Validation;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies.Execution;

/// <summary>
///     Free money of the bot: the balance less what entries spent and standing limit entries reserve. The position node is evaluated
///     before the order nodes, so an order placed on an event shows in the free money from the next event on.
/// </summary>
public class FreeCashTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");

    // Scenario "Свободные деньги без позиции".
    [Test]
    public void Run_that_just_started_has_the_balance_free()
    {
        StrategyRun run = BuyBelow200(StrategyDirection.Long, Fees(MarketCategory.Spot));

        run.Handle(new RunStarted(Start));

        Free(run).ShouldBe(1000m);
    }

    // Scenario "Резерв лимитки".
    [Test]
    public void Standing_limit_buy_reserves_its_value_until_the_cancel_is_confirmed()
    {
        StrategyRun run = BuyBelow200(StrategyDirection.Long, Fees(MarketCategory.Spot));

        Close(run, 99m).ShouldBe([new PlaceOrder("o-1", "hold", OrderSide.Buy, OrderType.Limit, 99m, 1m, null)]);
        Close(run, 99m).ShouldBeEmpty();
        Free(run).ShouldBe(901m);

        Close(run, 300m).ShouldBe([new CancelOrder("o-1")]);
        Free(run).ShouldBe(901m);

        run.Handle(new OrderCancelled(Start, "o-1"));
        Free(run).ShouldBe(1000m);
    }

    [Test]
    public void Reserve_of_a_limit_entry_includes_its_fee_in_the_quote_asset()
    {
        StrategyRun run = BuyBelow200(StrategyDirection.Long, Fees(MarketCategory.Linear));

        Close(run, 99m);

        Close(run, 99m);

        Free(run).ShouldBe(1000m - 99m - 99m * 0.0002m);
    }

    [Test]
    public void Unfilled_market_entry_reserves_its_amount_and_limit_exits_reserve_nothing()
    {
        Graph graph = new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("flat", SimpleNodeKind.Not)
            .Node("enter", SimpleNodeKind.MarketOrder, new NodeSettings(Side: OrderSide.Buy, SizeMode: OrderSizeMode.Amount), ("size", Values.Number(100m)))
            .Node("sell", SimpleNodeKind.HoldLimit, new NodeSettings(Side: OrderSide.Sell, SizeMode: OrderSizeMode.Quantity), ("price", Values.Number(150m)))
            .Wire("position.hasPosition", "flat.value")
            .Wire("flat.result", "enter.when")
            .Wire("position.hasPosition", "sell.when")
            .Wire("position.quantity", "sell.size")
            .Build();
        var run = StrategyRun.Start(StrategyDefinitions.Long(graph), CompositeCatalog.Empty, new Dictionary<string, NodeValue>(), Conditions.Neutral with { Balance = 1000m });
        run.Handle(new RunStarted(Start)).ShouldHaveSingleItem();

        Close(run, 100m);
        Free(run).ShouldBe(900m);

        run.Handle(new OrderFilled(Start, "o-1", 1m, 100m, IsComplete: true)).ShouldHaveSingleItem();
        Close(run, 100m);
        Free(run).ShouldBe(900m);
    }

    [Test]
    public void Long_entry_spends_and_exit_returns_the_value_less_the_quote_fee()
    {
        StrategyRun run = BuyBelow200(StrategyDirection.Long, Fees(MarketCategory.Spot));
        Close(run, 100m);

        run.Handle(new OrderFilled(Start, "o-1", 1m, 100m, IsComplete: true, Fee: 0.001m, FeeAsset: FeeAsset.Base));
        Free(run).ShouldBe(900m);

        run.Handle(new OrderFilled(Start, "o-2", 0.999m, 110m, IsComplete: true, Fee: 0.10989m, FeeAsset: FeeAsset.Quote));
        Free(run).ShouldBe(900m + 109.89m - 0.10989m);
    }

    [Test]
    public void Short_exit_returns_the_collateral_and_the_result_less_the_fees()
    {
        StrategyRun run = BuyBelow200(StrategyDirection.Short, Fees(MarketCategory.Linear));
        Close(run, 100m);

        run.Handle(new OrderFilled(Start, "o-1", 1m, 100m, IsComplete: true, Fee: 0.055m, FeeAsset: FeeAsset.Quote));
        Free(run).ShouldBe(899.945m);
        run.State.ShouldBe(new RunState(StrategyDirection.Short, 899.945m, 1m, 99.945m, 100m, 0m, 0m, 0m, 0));

        run.Handle(new OrderFilled(Start, "o-2", 0.5m, 90m, IsComplete: false, Fee: 0.02475m, FeeAsset: FeeAsset.Quote));
        Free(run).ShouldBe(899.945m + 50m + 5m - 0.02475m);
        run.Handle(new OrderFilled(Start, "o-2", 0.5m, 90m, IsComplete: true, Fee: 0.02475m, FeeAsset: FeeAsset.Quote));
        Free(run).ShouldBe(1000m + 10m - 0.055m - 0.0495m);
    }

    /// <summary>Holds an entry of one unit at the last close while it is below 200, and exits the position by market at once.</summary>
    private static StrategyRun BuyBelow200(StrategyDirection direction, FeeSchedule fees)
    {
        OrderSide entry = direction == StrategyDirection.Long ? OrderSide.Buy : OrderSide.Sell;
        Graph graph = new GraphBuilder()
            .Node("candle", SimpleNodeKind.Candle)
            .Node("position", SimpleNodeKind.Position)
            .Node("cheap", SimpleNodeKind.Compare, new NodeSettings(Operation: CompareOperation.Less), ("b", Values.Number(200m)))
            .Node("flat", SimpleNodeKind.Not)
            .Node("enter", SimpleNodeKind.And)
            .Node("hold", SimpleNodeKind.HoldLimit, new NodeSettings(Side: entry, SizeMode: OrderSizeMode.Quantity), ("size", Values.Number(1m)))
            .Node("exit", SimpleNodeKind.ClosePosition, ("when", Values.Boolean(true)))
            .Wire("candle.close", "cheap.a")
            .Wire("position.hasPosition", "flat.value")
            .Wire("cheap.result", "enter.a")
            .Wire("flat.result", "enter.b")
            .Wire("enter.result", "hold.when")
            .Wire("candle.close", "hold.price")
            .Build();

        return StrategyRun.Start(new StrategyDefinition(direction, [], graph), CompositeCatalog.Empty, new Dictionary<string, NodeValue>(), Conditions.Neutral with { Balance = 1000m, Fees = fees });
    }

    private static FeeSchedule Fees(MarketCategory category)
    {
        return new FeeSchedule(0.0002m, 0.00055m, category);
    }

    private static IReadOnlyList<OrderCommand> Close(StrategyRun run, decimal close)
    {
        return run.Handle(new BarClosed(Start, new Bar(close, close, close, close, 1m)));
    }

    private static decimal Free(StrategyRun run)
    {
        return ((NumberValue)run.ValueOf("position", "free")!).Value;
    }
}