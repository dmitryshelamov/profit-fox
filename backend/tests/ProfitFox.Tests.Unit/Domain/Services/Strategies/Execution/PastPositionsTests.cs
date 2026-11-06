using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.Services.Strategies.Validation;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies.Execution;

/// <summary>The outputs of the position node about the positions before the current one.</summary>
public class PastPositionsTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
    private static readonly IReadOnlyDictionary<string, NodeValue> NoValues = new Dictionary<string, NodeValue>();

    // Scenario "Две закрытые позиции".
    [Test]
    public void Closed_positions_and_their_pnl_leave_out_the_current_position()
    {
        StrategyRun run = EnterWhenFlat();
        run.Handle(new RunStarted(Start));
        Exit(run, "o-1", "o-2", 105m);
        Exit(run, "o-3", "o-4", 98m);
        run.Handle(new OrderFilled(Start, "o-5", 2m, 100m, IsComplete: true));
        run.Close(Start);

        run.Handle(new OrderFilled(Start, "o-6", 1m, 101m, IsComplete: false));

        run.State.Position.ShouldBe(3);
        run.State.RealizedPnl.ShouldBe(1m);
        run.ValueOf("position", "closedPositions").ShouldBe(new NumberValue(2m));
        run.ValueOf("position", "closedPnl").ShouldBe(new NumberValue(3m));
    }

    // Scenario "Прошлые позиции после старта с сохранённой позицией".
    [Test]
    public void Restored_run_reports_the_positions_before_the_saved_one()
    {
        var run = StrategyRun.Restore(
            StrategyDefinitions.Long(Graph()),
            CompositeCatalog.Empty,
            NoValues,
            Conditions.Neutral,
            new RunSnapshot(4, 0m, 0m, 0m, PastPnl: 12m, 0m, 0m, null, null, false, 0, [], 0));

        run.Handle(new RunStarted(Start));

        run.ValueOf("position", "closedPositions").ShouldBe(new NumberValue(3m));
        run.ValueOf("position", "closedPnl").ShouldBe(new NumberValue(12m));
    }

    /// <summary>Fills the entry bought at 100, closes the position as its owner and fills the sell at <paramref name="price" />.</summary>
    private static void Exit(StrategyRun run, string entry, string exit, decimal price)
    {
        run.Handle(new OrderFilled(Start, entry, 1m, 100m, IsComplete: true));
        run.Close(Start);
        run.Handle(new OrderFilled(Start, exit, 1m, price, IsComplete: true));
    }

    private static StrategyRun EnterWhenFlat()
    {
        return StrategyRun.Start(StrategyDefinitions.Long(Graph()), CompositeCatalog.Empty, NoValues, Conditions.Neutral);
    }

    private static Graph Graph()
    {
        return new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("flat", SimpleNodeKind.Not)
            .Node("enter", SimpleNodeKind.MarketOrder, new NodeSettings(Side: OrderSide.Buy, SizeMode: OrderSizeMode.Quantity), ("size", Values.Number(2m)))
            .Wire("position.hasPosition", "flat.value")
            .Wire("flat.result", "enter.when")
            .Build();
    }
}