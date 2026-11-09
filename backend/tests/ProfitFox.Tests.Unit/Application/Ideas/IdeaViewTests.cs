using ProfitFox.Application.Ideas;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Ideas;

/// <summary>
///     Requirement "Безубыток идеи": an idea breaks even by the fees of its category; decision 5 of add-own-idea-numbers: it carries its own
///     numbers from its bucket over 90 days and the curve of its set.
/// </summary>
public class IdeaViewTests
{
    private static readonly DateTimeOffset Noon = DateTimeOffset.Parse("2024-03-10T12:00:00Z");
    private static readonly Dictionary<MarketCategory, FeeRates> Fees = new Dictionary<MarketCategory, FeeRates> { [MarketCategory.Linear] = new FeeRates(0.02m, 0.055m) };

    // Scenario "Безубыток с комиссиями": linear long 100 → 98 / 104 with the taker 0.055 %.
    [Test]
    public void Idea_breaks_even_by_the_fees_of_its_category()
    {
        IdeaView view = IdeaView.Of([Idea(new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, 104m))], [], [], Fees).Single();

        view.Shares.BreakEven.ShouldBe(0.021089m / (0.038878m + 0.021089m));
    }

    // Scenario "Цель не окупает комиссии": a target 0.1 % away pays less than the two takers.
    [Test]
    public void Idea_whose_target_does_not_cover_the_fees_has_no_break_even()
    {
        IdeaView view = IdeaView.Of([Idea(new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 99.95m, 100.1m))], [], [], Fees).Single();

        view.Shares.BreakEven.ShouldBeNull();
    }

    // Scenario "Мало своих данных" of "Вероятность идеи": a bucket of 12 cases of its own has the probability of the model by its parent; the
    // threshold of 30 own cases is gone.
    [Test]
    public void Idea_of_a_bucket_of_few_own_cases_has_the_probability_of_the_model()
    {
        Idea idea = Idea(new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, 104m));
        var bucket = new BucketStatistics(12, 6, 6, 0, 0.28m, 0.2m, 0.36m, -0.1m, -0.3m, 0.1m) { FewOwnCases = true, GrossExpectancy = 0m };

        IdeaView view = IdeaView.Of([idea], [new IdeaBucket(KeyOf(idea), 90, bucket)], [], Fees).Single();

        view.Numbers.Probability.ShouldBe(0.28m);
    }

    // Scenario "Дальняя цель «Уровней»": the idea of «Уровни» with R:R 4 takes the multiplier 0.675 of the band from 3 to 6 of the curve of its
    // interval, side and category; the curves of another interval, side or category do not count.
    [Test]
    public void Idea_of_levels_has_the_probability_of_its_bucket_times_the_multiplier_of_its_band()
    {
        Idea idea = Idea(new IdeaPlan(IdeaType.Levels, IdeaSide.Long, 100m, 98m, 108m));
        var bucket = new BucketStatistics(400, 88, 300, 12, 0.22m, 0.18m, 0.26m, -0.1m, -0.2m, 0m) { GrossExpectancy = 0m };
        RiskRewardCurve curve = Curve(idea.Interval, (400, 110), (300, 57), (200, 27), (100, 6));
        RiskRewardCurve other = Curve(idea.Interval == Interval.OneHour ? Interval.FourHours : Interval.OneHour, (400, 0), (300, 0), (200, 200), (100, 0));
        RiskRewardCurve[] others = [other, other with { Interval = idea.Interval, Side = IdeaSide.Short }, other with { Interval = idea.Interval, Category = MarketCategory.Spot }];

        IdeaView view = IdeaView.Of([idea], [new IdeaBucket(KeyOf(idea), 90, bucket)], [.. others, curve], Fees).Single();

        (view.Numbers.Multiplier, view.Numbers.Probability).ShouldBe((0.675m, 0.1485m));
    }

    private static IdeaBucketKey KeyOf(Idea idea)
    {
        return new IdeaBucketKey(idea.Setup, idea.Track.Plan.Type, idea.Interval, idea.Track.Plan.Side, idea.Instrument.Category);
    }

    private static RiskRewardCurve Curve(Interval interval, params (int Cases, int Targets)[] bands)
    {
        return new RiskRewardCurve(
            interval,
            IdeaSide.Long,
            MarketCategory.Linear,
            [.. bands.Select((band, index) => new RiskRewardBandCount((RiskRewardBand)index, band.Cases, band.Targets))]);
    }

    private static Idea Idea(IdeaPlan plan)
    {
        Idea idea = IdeaGenerator.Generate(Noon);

        return idea with { Track = idea.Track with { Plan = plan } };
    }
}