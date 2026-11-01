using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.Bots.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.ValueObjects.Bots;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Entities.Strategies;

public class StrategyLibraryBotsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
    private static readonly Guid Ema = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid EmaShort = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid BotId = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly Instrument BtcSpot = new Instrument(MarketCategory.Spot, "BTCUSDT");

    // Scenario "Валидный бот".
    [Test]
    public void Valid_bot_is_added_with_only_the_given_values()
    {
        StrategyLibrary library = Library();

        Bot bot = library.AddBot(BotId, "EMA BTC", Ema, Fast(10m), BtcSpot, Interval.OneHour, 1000m, Now);

        bot.ShouldBe(new Bot(BotId, "EMA BTC", Ema, bot.ParameterValues, BtcSpot, Interval.OneHour, 1000m, Now, Now));
        bot.ParameterValues.ShouldBe(Fast(10m));
        library.Bots.ShouldBe([bot]);
    }

    // Scenario "Значение вне ограничений".
    [Test]
    public void Value_outside_the_limits_is_rejected()
    {
        StrategyLibrary library = Library();

        BotValidationException exception = Should.Throw<BotValidationException>(() =>
            library.AddBot(BotId, "EMA BTC", Ema, Fast(1m), BtcSpot, Interval.OneHour, 1000m, Now));

        exception.Errors.ShouldBe([new BotError(BotField.Parameter, "fast", "Value must be at least 2.")]);
        library.Bots.ShouldBeEmpty();
    }

    [Test]
    public void Value_of_an_unknown_parameter_is_rejected()
    {
        BotValidationException exception = Should.Throw<BotValidationException>(() => Library()
            .AddBot(BotId, "EMA BTC", Ema, new Dictionary<string, NodeValue> { ["medium"] = Values.Number(5m) }, BtcSpot, Interval.OneHour, 1000m, Now));

        exception.Errors.ShouldBe([new BotError(BotField.Parameter, "medium", "No such parameter.")]);
    }

    // Scenario "Шорт на споте".
    [Test]
    public void Short_strategy_on_spot_is_rejected()
    {
        BotValidationException exception = Should.Throw<BotValidationException>(() =>
            Library().AddBot(BotId, "Short", EmaShort, NoValues, BtcSpot, Interval.OneHour, 1000m, Now));

        exception.Errors.ShouldBe([new BotError(BotField.Category, null, "The strategy direction does not support this market category.")]);
    }

    // Scenario "Неизвестная стратегия".
    [Test]
    public void Unknown_strategy_is_rejected()
    {
        BotValidationException exception = Should.Throw<BotValidationException>(() =>
            Library().AddBot(BotId, "X", Guid.NewGuid(), Fast(10m), BtcSpot, Interval.OneHour, 1000m, Now));

        exception.Errors.ShouldBe([new BotError(BotField.Strategy, null, "No such strategy.")]);
    }

    // Scenario "Нулевой баланс".
    [TestCase(0)]
    [TestCase(-1)]
    public void Balance_that_is_not_positive_is_rejected(decimal balance)
    {
        BotValidationException exception = Should.Throw<BotValidationException>(() =>
            Library().AddBot(BotId, "EMA BTC", Ema, NoValues, BtcSpot, Interval.OneHour, balance, Now));

        exception.Errors.ShouldBe([new BotError(BotField.Balance, null, "Balance must be greater than zero.")]);
    }

    [Test]
    public void All_errors_are_reported_at_once()
    {
        BotValidationException exception = Should.Throw<BotValidationException>(() =>
            Library().AddBot(BotId, "Short", EmaShort, Fast(1m), BtcSpot, Interval.OneHour, 0m, Now));

        exception.Errors.Select(error => error.Field).ShouldBe([BotField.Parameter, BotField.Category, BotField.Balance]);
    }

    [Test]
    public void Bot_is_changed_and_removed()
    {
        StrategyLibrary library = Library();
        library.AddBot(BotId, "EMA BTC", Ema, Fast(10m), BtcSpot, Interval.OneHour, 1000m, Now);
        var ethSpot = new Instrument(MarketCategory.Spot, "ETHUSDT");

        Bot changed = library.UpdateBot(BotId, "EMA ETH", Fast(12m), ethSpot, Interval.FourHours, 500m, Now.AddHours(1))!;

        changed.ShouldBe(new Bot(BotId, "EMA ETH", Ema, changed.ParameterValues, ethSpot, Interval.FourHours, 500m, Now, Now.AddHours(1)));
        changed.ParameterValues.ShouldBe(Fast(12m));
        library.Bots.ShouldBe([changed]);
        library.RemoveBot(BotId).ShouldBeTrue();
        library.Bots.ShouldBeEmpty();
    }

    // Scenario "Значение по умолчанию".
    [Test]
    public void Parameter_without_a_bot_value_takes_the_default_of_the_latest_version()
    {
        StrategyLibrary library = Library();
        Bot bot = library.AddBot(BotId, "EMA BTC", Ema, Fast(10m), BtcSpot, Interval.OneHour, 1000m, Now);
        StrategyDefinition ema = StrategyJson.ParseStrategy(StrategyFixtures.Element("ema"));
        StrategyDefinition slower = ema with
        {
            Parameters = [.. ema.Parameters.Select(parameter => parameter.Name == "slow" ? parameter with { Default = Values.Number(30m) } : parameter)]
        };

        library.UpdateStrategy(Ema, "EMA", slower, Now.AddHours(1));

        library.ParameterValues(bot).ShouldBe(
            new Dictionary<string, NodeValue> { ["fast"] = Values.Number(10m), ["slow"] = Values.Number(30m), ["amount"] = Values.Number(100m) });
    }

    // Scenario "Изменение с ошибкой".
    [Test]
    public void Invalid_change_keeps_the_bot()
    {
        StrategyLibrary library = Library();
        Bot bot = library.AddBot(BotId, "EMA BTC", Ema, Fast(10m), BtcSpot, Interval.OneHour, 1000m, Now);

        BotValidationException exception = Should.Throw<BotValidationException>(() =>
            library.UpdateBot(BotId, "EMA BTC", Fast(1m), BtcSpot, Interval.OneHour, 1000m, Now.AddHours(1)));

        exception.Errors.ShouldBe([new BotError(BotField.Parameter, "fast", "Value must be at least 2.")]);
        library.Bots.ShouldBe([bot]);
    }

    // Scenario "Удалён параметр со значением у бота".
    [Test]
    public void Removing_a_parameter_a_bot_sets_is_rejected_with_the_bot_and_the_strategy_stays()
    {
        StrategyLibrary library = Library();
        library.AddBot(BotId, "EMA BTC", Ema, Fast(10m), BtcSpot, Interval.OneHour, 1000m, Now);
        Strategy before = library.Strategies.Single(strategy => strategy.Id == Ema);
        StrategyDefinition withoutFast = before.Definition with
        {
            Parameters = [.. before.Definition.Parameters.Where(parameter => parameter.Name != "fast")],
            Graph = before.Definition.Graph with
            {
                Wires = [.. before.Definition.Graph.Wires.Where(wire => wire.From != new PinRef("params", "fast"))],
                Nodes = [.. before.Definition.Graph.Nodes.Select(node => node.Id == "cross" ? node with { Inputs = Fast(5m) } : node)]
            }
        };

        BotsAffectedException exception = Should.Throw<BotsAffectedException>(() => library.UpdateStrategy(Ema, "EMA", withoutFast, Now.AddHours(1)));

        exception.Bots.ShouldBe([new AffectedBot(BotId, "EMA BTC", exception.Bots[0].Errors)]);
        exception.Bots[0].Errors.ShouldBe([new BotError(BotField.Parameter, "fast", "No such parameter.")]);
        library.Strategies.Single(strategy => strategy.Id == Ema).ShouldBe(before);
        library.LatestVersion(Ema)!.Number.ShouldBe(1);
    }

    // Scenario "Смена направления".
    [Test]
    public void Changing_a_long_strategy_with_a_spot_bot_to_short_is_rejected()
    {
        StrategyLibrary library = Library();
        library.AddBot(BotId, "EMA BTC", Ema, NoValues, BtcSpot, Interval.OneHour, 1000m, Now);
        Strategy ema = library.Strategies.Single(strategy => strategy.Id == Ema);

        BotsAffectedException exception = Should.Throw<BotsAffectedException>(() =>
            library.UpdateStrategy(Ema, "EMA", ema.Definition with { Direction = StrategyDirection.Short }, Now.AddHours(1)));

        exception.Bots.ShouldHaveSingleItem().Errors.ShouldBe([new BotError(BotField.Category, null, "The strategy direction does not support this market category.")]);
    }

    // Scenario "Совместимое изменение".
    [Test]
    public void Compatible_change_is_saved_and_the_bot_follows_the_new_version()
    {
        StrategyLibrary library = Library();
        Bot bot = library.AddBot(BotId, "EMA BTC", Ema, Fast(10m), BtcSpot, Interval.OneHour, 1000m, Now);
        Strategy ema = library.Strategies.Single(strategy => strategy.Id == Ema);
        StrategyDefinition withNode = ema.Definition with
        {
            Graph = ema.Definition.Graph with { Nodes = [.. ema.Definition.Graph.Nodes, new GraphNode("extra", new SimpleNodeType(SimpleNodeKind.Candle), NoValues, new NodeSettings())] }
        };

        library.UpdateStrategy(Ema, "EMA", withNode, Now.AddHours(1));

        library.LatestVersion(Ema)!.Number.ShouldBe(2);
        library.Bots.ShouldBe([bot]);
    }

    // Scenario "Удаление стратегии с ботами".
    [Test]
    public void Strategy_with_bots_is_not_removed()
    {
        StrategyLibrary library = Library();
        library.AddBot(BotId, "EMA BTC", Ema, NoValues, BtcSpot, Interval.OneHour, 1000m, Now);

        BotsAffectedException exception = Should.Throw<BotsAffectedException>(() => library.RemoveStrategy(Ema));

        AffectedBot affected = exception.Bots.ShouldHaveSingleItem();
        (affected.Id, affected.Name, affected.Errors.Count).ShouldBe((BotId, "EMA BTC", 0));
        library.Strategies.Select(strategy => strategy.Id).ShouldContain(Ema);
        library.Change.RemovedStrategies.ShouldBeEmpty();
    }

    [Test]
    public void Missing_bot_is_not_changed_or_removed()
    {
        StrategyLibrary library = Library();

        library.UpdateBot(BotId, "X", NoValues, BtcSpot, Interval.OneHour, 1000m, Now).ShouldBeNull();
        library.RemoveBot(BotId).ShouldBeFalse();
    }

    private static StrategyLibrary Library()
    {
        var library = new StrategyLibrary(0, [], []);
        library.AddStrategy(Ema, "EMA", StrategyJson.ParseStrategy(StrategyFixtures.Element("ema")), Now);
        library.AddStrategy(EmaShort, "EMA Short", StrategyJson.ParseStrategy(StrategyFixtures.Element("ema-short")), Now);

        return library;
    }

    private static Dictionary<string, NodeValue> Fast(decimal value)
    {
        return new Dictionary<string, NodeValue> { ["fast"] = Values.Number(value) };
    }

    private static readonly IReadOnlyDictionary<string, NodeValue> NoValues = new Dictionary<string, NodeValue>();
}