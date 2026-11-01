using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.BuiltIns;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.Services.Strategies.Validation;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies.Execution;

/// <summary>
///     Reference strategies from the JSON fixtures on reference event sequences with precomputed commands, on the reference
///     instrument: prices on a tick of 0.01, quantities on a step of 0.001.
/// </summary>
public class ReferenceStrategiesTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");

    [TestCase("ema")]
    [TestCase("ema-short")]
    [TestCase("dca")]
    [TestCase("ladder")]
    public void Reference_strategy_is_valid(string name)
    {
        StrategyDefinition definition = StrategyJson.ParseStrategy(StrategyFixtures.Element(name));

        GraphValidator.ValidateStrategy(definition, BuiltInComposites.Catalog).ShouldBeEmpty();
    }

    [Test]
    public void Ema_buys_on_the_cross_up_and_sells_everything_on_the_cross_down()
    {
        StrategyRun run = Run("ema", ("fast", 2m), ("slow", 3m));
        decimal[] closes = [10m, 9m, 8m, 7m, 8m, 10m, 12m, 11m, 9m, 7m];
        var commands = new List<(int Bar, OrderCommand Command)>();

        for (int bar = 1; bar <= closes.Length; bar++)
        {
            commands.AddRange(Close(run, bar, closes[bar - 1]).Select(command => (bar, command)));

            if (bar == 7)
            {
                run.Handle(new OrderFilled(At(bar), "o-1", 10m, 10m, IsComplete: true)).ShouldBeEmpty();
            }
        }

        commands.ShouldBe(
        [
            (6, new PlaceOrder("o-1", "enter", OrderSide.Buy, OrderType.Market, null, null, 100m)),
            (9, new PlaceOrder("o-2", "exit", OrderSide.Sell, OrderType.Market, null, 10m, null))
        ]);
    }

    [Test]
    public void Ema_short_sells_on_the_cross_down_and_buys_back_on_the_cross_up()
    {
        StrategyRun run = Run("ema-short", ("fast", 2m), ("slow", 3m));
        decimal[] closes = [10m, 11m, 12m, 13m, 12m, 10m, 8m, 9m, 11m, 13m];
        var commands = new List<(int Bar, OrderCommand Command)>();

        for (int bar = 1; bar <= closes.Length; bar++)
        {
            commands.AddRange(Close(run, bar, closes[bar - 1]).Select(command => (bar, command)));

            if (bar == 7)
            {
                run.Handle(new OrderFilled(At(bar), "o-1", 10m, 10m, IsComplete: true)).ShouldBeEmpty();
            }
        }

        commands.ShouldBe(
        [
            (6, new PlaceOrder("o-1", "enter", OrderSide.Sell, OrderType.Market, null, null, 100m)),
            (9, new PlaceOrder("o-2", "exit", OrderSide.Buy, OrderType.Market, null, 10m, null))
        ]);
    }

    [Test]
    public void Dca_buys_on_start_and_after_the_interval_until_the_deposit_is_spent()
    {
        StrategyRun run = Run("dca");

        run.Handle(new RunStarted(Start)).ShouldBe([Market("o-1", "dca/buy", 100m)]);
        run.Handle(new OrderFilled(Start, "o-1", 1m, 100m, IsComplete: true)).ShouldBeEmpty();
        run.Handle(new BarClosed(Start.AddMinutes(30), Bar(100m))).ShouldBeEmpty();
        run.Handle(new BarClosed(Start.AddHours(1), Bar(100m))).ShouldBe([Market("o-2", "dca/buy", 100m)]);
        run.Handle(new OrderFilled(Start.AddHours(1), "o-2", 1m, 100m, IsComplete: true)).ShouldBeEmpty();
        run.Handle(new BarClosed(Start.AddHours(2), Bar(100m))).ShouldBeEmpty();
        run.Handle(new BarClosed(Start.AddHours(3), Bar(100m))).ShouldBeEmpty();
    }

    [Test]
    public void Ladder_holds_a_sell_above_the_average_and_an_averaging_buy_below_the_last_entry()
    {
        StrategyRun run = Run("ladder");

        run.Handle(new RunStarted(Start)).ShouldBe([Market("o-1", "ladder/entry", 100m)]);

        run.Handle(new OrderFilled(Start, "o-1", 1m, 100m, IsComplete: true)).ShouldBe(
            [Limit("o-2", "ladder/buy", OrderSide.Buy, 99m, 1.010m), Limit("o-3", "ladder/sell", OrderSide.Sell, 101m, 1m)],
            ignoreOrder: true);

        run.Handle(new OrderFilled(Start, "o-2", 1.010m, 99m, IsComplete: true)).ShouldBe(
            [new CancelOrder("o-3"), Limit("o-4", "ladder/buy", OrderSide.Buy, 98.01m, 1.020m)],
            ignoreOrder: true);

        // Average price 199.99 / 2.010 = 99.4975…, plus 1 % is 100.4924…, rounded up to the tick.
        run.Handle(new OrderCancelled(Start, "o-3"))
            .ShouldBe([Limit("o-5", "ladder/sell", OrderSide.Sell, 100.50m, 2.010m)]);
    }

    [Test]
    public void Ladder_gives_the_same_commands_on_a_second_run()
    {
        StrategyEvent[] events =
        [
            new RunStarted(Start),
            new OrderFilled(Start, "o-1", 1m, 100m, IsComplete: true),
            new OrderFilled(Start, "o-2", 1.010m, 99m, IsComplete: true),
            new OrderCancelled(Start, "o-3"),
            new BarClosed(Start.AddHours(1), Bar(98m))
        ];

        IReadOnlyList<OrderCommand> first = [.. events.SelectMany(Run("ladder").Handle)];
        IReadOnlyList<OrderCommand> second = [.. events.SelectMany(Run("ladder").Handle)];

        first.Count.ShouldBe(6);
        second.ShouldBe(first);
    }

    [Test]
    public void Ladder_starts_a_new_cycle_after_the_sell()
    {
        StrategyRun run = Run("ladder");
        run.Handle(new RunStarted(Start));
        run.Handle(new OrderFilled(Start, "o-1", 1m, 100m, IsComplete: true));

        IReadOnlyList<OrderCommand> commands = run.Handle(new OrderFilled(Start, "o-3", 1m, 101m, IsComplete: true));

        commands.ShouldBe([new CancelOrder("o-2"), Market("o-4", "ladder/entry", 100m)], ignoreOrder: true);
    }

    private static StrategyRun Run(string name, params (string Name, decimal Value)[] values)
    {
        StrategyDefinition definition = StrategyJson.ParseStrategy(StrategyFixtures.Element(name));

        return StrategyRun.Start(
            definition,
            BuiltInComposites.Catalog,
            values.ToDictionary(value => value.Name, value => (NodeValue)new NumberValue(value.Value)),
            Conditions.Reference);
    }

    private static IReadOnlyList<OrderCommand> Close(StrategyRun run, int bar, decimal close)
    {
        return run.Handle(new BarClosed(At(bar), Bar(close)));
    }

    private static DateTimeOffset At(int bar)
    {
        return Start.AddHours(bar);
    }

    private static Bar Bar(decimal close)
    {
        return new Bar(close, close, close, close, 1m);
    }

    private static PlaceOrder Market(string id, string node, decimal amount)
    {
        return new PlaceOrder(id, node, OrderSide.Buy, OrderType.Market, null, null, amount);
    }

    private static PlaceOrder Limit(string id, string node, OrderSide side, decimal price, decimal quantity)
    {
        return new PlaceOrder(id, node, side, OrderType.Limit, price, quantity, null);
    }
}