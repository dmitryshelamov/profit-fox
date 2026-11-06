using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.Services.Strategies.Validation;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies.Execution;

/// <summary>The output of the position node that counts the closed bars after the last entry.</summary>
public class BarsSinceEntryTests
{
    private static readonly DateTimeOffset Entry = DateTimeOffset.Parse("2024-01-01T10:20:00Z");
    private static readonly IReadOnlyDictionary<string, NodeValue> NoValues = new Dictionary<string, NodeValue>();

    // Scenario "Счёт баров".
    [Test]
    public void Bars_closed_after_the_entry_are_counted()
    {
        var run = StrategyRun.Start(StrategyDefinitions.Long(Graph()), CompositeCatalog.Empty, NoValues, Conditions.Neutral);
        run.Handle(new RunStarted(Entry.AddHours(-1)));
        run.Handle(new BarClosed(Entry.AddMinutes(-20), Bar()));
        run.Handle(new OrderFilled(Entry, "o-1", 1m, 100m, IsComplete: true));

        run.Handle(new BarClosed(At(11), Bar()));
        run.Handle(new BarClosed(At(12), Bar()));
        run.Handle(new BarClosed(At(13), Bar()));

        run.ValueOf("position", "barsSinceEntry").ShouldBe(new NumberValue(3m));
    }

    [Test]
    public void A_new_entry_starts_the_count_again()
    {
        var run = StrategyRun.Start(StrategyDefinitions.Long(Graph()), CompositeCatalog.Empty, NoValues, Conditions.Neutral);
        run.Handle(new RunStarted(Entry));
        run.Handle(new OrderFilled(Entry, "o-1", 1m, 100m, IsComplete: false));
        run.Handle(new BarClosed(At(11), Bar()));

        run.Handle(new OrderFilled(At(11).AddMinutes(5), "o-1", 1m, 100m, IsComplete: true));

        run.ValueOf("position", "barsSinceEntry").ShouldBe(new NumberValue(0m));
    }

    [Test]
    public void Without_a_position_there_is_no_count()
    {
        var run = StrategyRun.Start(StrategyDefinitions.Long(Graph()), CompositeCatalog.Empty, NoValues, Conditions.Neutral);

        run.Handle(new BarClosed(At(11), Bar()));

        run.ValueOf("position", "barsSinceEntry").ShouldBeNull();
    }

    // Scenario "После рестарта".
    [Test]
    public void Warm_up_counts_the_bars_after_the_entry_of_a_restored_position()
    {
        var run = StrategyRun.Restore(
            StrategyDefinitions.Long(Graph()),
            CompositeCatalog.Empty,
            NoValues,
            Conditions.Neutral,
            new RunSnapshot(1, 1m, 100m, 100m, 0m, 0m, 0m, 100m, Entry, false, 0, [], 1));

        run.WarmUp(new BarClosed(At(10), Bar()));
        run.WarmUp(new BarClosed(At(11), Bar()));
        run.WarmUp(new BarClosed(At(12), Bar()));
        run.WarmUp(new BarClosed(At(13), Bar()));
        run.Handle(new BarClosed(At(14), Bar()));

        run.ValueOf("position", "barsSinceEntry").ShouldBe(new NumberValue(4m));
    }

    private static DateTimeOffset At(int hour)
    {
        return new DateTimeOffset(Entry.Year, Entry.Month, Entry.Day, hour, 0, 0, TimeSpan.Zero);
    }

    private static Bar Bar()
    {
        return new Bar(100m, 100m, 100m, 100m, 1m);
    }

    private static Graph Graph()
    {
        return new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("flat", SimpleNodeKind.Not)
            .Node("enter", SimpleNodeKind.MarketOrder, new NodeSettings(Side: OrderSide.Buy, SizeMode: OrderSizeMode.Quantity), ("size", Values.Number(1m)))
            .Wire("position.hasPosition", "flat.value")
            .Wire("flat.result", "enter.when")
            .Build();
    }
}