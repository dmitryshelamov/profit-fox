using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Entities.Strategies;

public class StrategyLibraryStrategiesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
    private static readonly Guid Id = Guid.Parse("00000000-0000-0000-0000-000000000001");

    [Test]
    public void Valid_strategy_is_added()
    {
        var library = new StrategyLibrary(0, [], []);

        Strategy strategy = library.AddStrategy(Id, "EMA", Valid(), Now);

        strategy.ShouldBe(new Strategy(Id, "EMA", strategy.Definition, Now, Now));
        library.Strategies.ShouldBe([strategy]);
    }

    [Test]
    public void Strategy_with_errors_is_not_added()
    {
        var library = new StrategyLibrary(0, [], []);

        GraphValidationException exception = Should.Throw<GraphValidationException>(() => library.AddStrategy(Id, "EMA", Invalid(), Now));

        exception.Errors.ShouldNotBeEmpty();
        library.Strategies.ShouldBeEmpty();
    }

    [Test]
    public void Change_with_errors_keeps_the_strategy()
    {
        var library = new StrategyLibrary(0, [], []);
        Strategy original = library.AddStrategy(Id, "EMA", Valid(), Now);

        Should.Throw<GraphValidationException>(() => library.UpdateStrategy(Id, "EMA 2", Invalid(), Now.AddHours(1)));

        library.Strategies.ShouldBe([original]);
    }

    [Test]
    public void Valid_change_replaces_the_strategy()
    {
        var library = new StrategyLibrary(0, [], []);
        library.AddStrategy(Id, "EMA", Valid(), Now);

        Strategy changed = library.UpdateStrategy(Id, "EMA 2", Valid(), Now.AddHours(1))!;

        changed.ShouldBe(new Strategy(Id, "EMA 2", changed.Definition, Now, Now.AddHours(1)));
        library.Strategies.ShouldBe([changed]);
    }

    [Test]
    public void Missing_strategy_is_not_changed_or_removed()
    {
        var library = new StrategyLibrary(0, [], []);

        library.UpdateStrategy(Id, "EMA", Valid(), Now).ShouldBeNull();
        library.RemoveStrategy(Id).ShouldBeFalse();
    }

    [Test]
    public void Strategy_is_removed()
    {
        var library = new StrategyLibrary(0, [], []);
        library.AddStrategy(Id, "EMA", Valid(), Now);

        library.RemoveStrategy(Id).ShouldBeTrue();

        library.Strategies.ShouldBeEmpty();
    }

    private static StrategyDefinition Valid()
    {
        return StrategyDefinitions.Long(new GraphBuilder().Node("candle", SimpleNodeKind.Candle).Build());
    }

    private static StrategyDefinition Invalid()
    {
        return StrategyDefinitions.Long(new GraphBuilder().Node("mul", SimpleNodeKind.Multiply).Build());
    }
}