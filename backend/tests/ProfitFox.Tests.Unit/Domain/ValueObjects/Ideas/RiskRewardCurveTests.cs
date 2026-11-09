using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.ValueObjects.Ideas;

/// <summary>
///     Requirements "Кривая «Уровней» по R:R" and "Множитель полосы без данных" (decision 4 of add-own-idea-numbers): the share of targets of a
///     band of the reward to risk of the plan over that of its set, with the fallbacks of a band of fewer than 30 cases.
/// </summary>
public class RiskRewardCurveTests
{
    // Scenario "Граница полосы": the lower bound is in the band.
    [TestCase(1.8, RiskRewardBand.UpTo2)]
    [TestCase(2, RiskRewardBand.From2To3)]
    [TestCase(3, RiskRewardBand.From3To6)]
    [TestCase(6, RiskRewardBand.From6)]
    public void Reward_to_risk_falls_into_its_band(decimal riskReward, RiskRewardBand band)
    {
        RiskRewardBands.Of(riskReward).ShouldBe(band);
    }

    // Scenario "Множители полос": 1 000 cases and 200 targets.
    [Test]
    public void Multiplier_of_a_band_is_its_share_of_targets_over_that_of_the_set()
    {
        RiskRewardCurve curve = Curve((400, 110), (300, 57), (200, 27), (100, 6));

        new[] { RiskRewardBand.UpTo2, RiskRewardBand.From2To3, RiskRewardBand.From3To6, RiskRewardBand.From6 }
            .Select(curve.Multiplier)
            .ShouldBe([1.375m, 0.95m, 0.675m, 0.3m]);
    }

    // Scenario "Мало случаев в дальней полосе": the band of 12 cases takes the multiplier of the band below it.
    [Test]
    public void Band_of_few_cases_takes_the_multiplier_of_the_nearest_band_below()
    {
        Curve((400, 110), (388, 57), (200, 27), (12, 6)).Multiplier(RiskRewardBand.From6).ShouldBe(0.675m);
    }

    // The nearest band below with 30 cases and more: past a band below that has too few itself.
    [Test]
    public void Band_of_few_cases_passes_over_a_band_below_of_few_cases_too()
    {
        Curve((400, 110), (10, 2), (10, 2), (580, 86)).Multiplier(RiskRewardBand.From3To6).ShouldBe(1.375m);
    }

    // Scenario "Мало случаев в ближней полосе": nothing below the first band.
    [Test]
    public void First_band_of_few_cases_has_a_multiplier_of_one()
    {
        Curve((20, 5), (480, 95), (400, 80), (100, 20)).Multiplier(RiskRewardBand.UpTo2).ShouldBe(1m);
    }

    // A set without a single target has no share to divide by.
    [Test]
    public void Set_without_a_target_has_a_multiplier_of_one()
    {
        Curve((400, 0), (300, 0), (200, 0), (100, 0)).Multiplier(RiskRewardBand.UpTo2).ShouldBe(1m);
    }

    // Scenario "Набор без кривой": «Уровни» 4 h short spot without a case have no curve.
    [Test]
    public void Set_without_a_curve_has_a_multiplier_of_one()
    {
        RiskRewardCurve.MultiplierFor(null, 4m).ShouldBe(1m);
    }

    [Test]
    public void Multiplier_for_a_reward_to_risk_is_that_of_its_band()
    {
        RiskRewardCurve.MultiplierFor(Curve((400, 110), (300, 57), (200, 27), (100, 6)), 4m).ShouldBe(0.675m);
    }

    private static RiskRewardCurve Curve(params (int Cases, int Targets)[] bands)
    {
        return new RiskRewardCurve(
            Interval.OneHour,
            IdeaSide.Long,
            MarketCategory.Linear,
            [.. bands.Select((band, index) => new RiskRewardBandCount((RiskRewardBand)index, band.Cases, band.Targets))]);
    }
}