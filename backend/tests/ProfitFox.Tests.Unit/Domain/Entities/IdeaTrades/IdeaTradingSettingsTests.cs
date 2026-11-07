using ProfitFox.Domain.Entities.IdeaTrades;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Entities.IdeaTrades;

public class IdeaTradingSettingsTests
{
    // Scenario "Защиты по умолчанию": neither guard of the autos stops an entry until the owner sets it.
    [Test]
    public void Default_settings_have_both_guards_of_the_autos_off()
    {
        (IdeaTradingSettings.Default.DailyLossLimit, IdeaTradingSettings.Default.MaxSameDirection).ShouldBe((null, null));
    }

    // Requirement "Настройки торговли идеями": a trade on linear enters at 1× until the owner sets another leverage.
    [Test]
    public void Default_settings_have_the_leverage_of_one()
    {
        IdeaTradingSettings.Default.Leverage.ShouldBe(1);
    }
}