using ProfitFox.Application.Backtests;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Backtests;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Backtests;

/// <summary>Requirement "Проскальзывание идей": the slippage of a category comes from the settings next to its fees.</summary>
public class BacktestSettingsTests
{
    [Test]
    public void Policy_takes_the_slippage_of_a_category_with_its_fees()
    {
        var settings = new BacktestSettings
        {
            DefaultFees = new Dictionary<string, FeeRateSettings> { ["linear"] = new FeeRateSettings { Limit = 0.02m, Market = 0.055m, Slippage = 0.05m } }
        };

        settings.Policy().DefaultFees[MarketCategory.Linear].ShouldBe(new FeeRates(0.02m, 0.055m, 0.05m));
    }

    // Scenario "Нет проскальзывания в настройках": a service that lost the key would count the ideas without it while another counts with it.
    [TestCase("0.1", "0.05", true)]
    [TestCase("0", "0", true)]
    [TestCase(null, "0.05", false)]
    [TestCase("0.1", null, false)]
    [TestCase("-0.1", "0.05", false)]
    public void Idea_slippage_is_set_when_spot_and_linear_both_have_one_not_below_zero(string? spot, string? linear, bool set)
    {
        var settings = new BacktestSettings
        {
            DefaultFees = new Dictionary<string, FeeRateSettings>
            {
                ["spot"] = new FeeRateSettings { Limit = 0.1m, Market = 0.1m, Slippage = Rate(spot) },
                ["linear"] = new FeeRateSettings { Limit = 0.02m, Market = 0.055m, Slippage = Rate(linear) }
            }
        };

        settings.HasIdeaSlippage().ShouldBe(set);
    }

    [Test]
    public void Idea_slippage_is_not_set_without_a_category()
    {
        var settings = new BacktestSettings { DefaultFees = new Dictionary<string, FeeRateSettings> { ["linear"] = new FeeRateSettings { Slippage = 0.05m } } };

        settings.HasIdeaSlippage().ShouldBeFalse();
    }

    private static decimal? Rate(string? value)
    {
        return value is null ? null : decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    }
}