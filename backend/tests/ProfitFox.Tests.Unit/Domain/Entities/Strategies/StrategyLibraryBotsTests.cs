using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.Bots.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies;
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