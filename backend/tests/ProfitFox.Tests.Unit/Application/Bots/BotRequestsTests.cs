using ProfitFox.Application.Bots;
using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Bots;

public class BotRequestsTests
{
    // Scenario "Интервал не из списка MOEX".
    [Test]
    public void Interval_outside_the_list_of_the_category_is_an_error_of_the_interval()
    {
        ValidationFailedException error = Should.Throw<ValidationFailedException>(() => BotRequests.Read("SBER EMA", null, "moex", "SBER", "D", 1000m));

        error.Errors.ShouldBe(new Dictionary<string, string> { ["interval"] = "Interval must be one of the intervals of moex: 1, 3, 5, 15, 30, 60, 120, 240." });
    }

    [Test]
    public void Moex_share_on_an_hour_is_read()
    {
        BotSettings settings = BotRequests.Read("SBER EMA", null, "moex", "sber", "60", 1000m);

        (settings.Instrument, settings.Interval).ShouldBe((new Instrument(MarketCategory.Moex, "SBER"), Interval.OneHour));
    }

    [Test]
    public void Unknown_category_is_an_error_of_the_category()
    {
        ValidationFailedException error = Should.Throw<ValidationFailedException>(() => BotRequests.Read("Bot", null, "inverse", "BTCUSD", "60", 1000m));

        error.Errors["category"].ShouldBe("Category must be spot, linear or moex.");
    }
}