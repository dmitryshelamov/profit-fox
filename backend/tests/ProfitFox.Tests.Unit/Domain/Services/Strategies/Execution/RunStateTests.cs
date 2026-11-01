using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Execution;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies.Execution;

public class RunStateTests
{
    [Test]
    public void Long_is_worth_its_quantity_at_the_price_and_the_dust_counts_too()
    {
        var state = new RunState(StrategyDirection.Long, 900m, 1m, 100.1m, 100m, 0.0005m, 0m, 0);

        state.Equity(110m).ShouldBe(900m + 110m + 0.0005m * 110m);
        state.Unrealized(110m).ShouldBe(9.9m);
    }

    [Test]
    public void Short_is_worth_its_collateral_plus_the_fall_of_the_price()
    {
        // Sold 2 at 100 with a fee of 0.04: the cash paid 200.04 as collateral and fee, the invested amount is 199.96.
        var state = new RunState(StrategyDirection.Short, 799.96m, 2m, 199.96m, 200m, 0m, 0m, 0);

        state.Equity(90m).ShouldBe(799.96m + 200m + 20m);
        state.Unrealized(90m).ShouldBe(19.96m);
        state.Unrealized(100m).ShouldBe(-0.04m);
    }
}