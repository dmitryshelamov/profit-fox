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
}