using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Entities.Ideas;

/// <summary>Decision 3: an idea and a signal keep the numbers of the market on the minute they fired, and their features come from them.</summary>
public class IdeaTests
{
    private static readonly DateTimeOffset Fired = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public void The_level_of_an_idea_is_that_of_its_numbers()
    {
        // Scenario "Уровень": a day drop and a high funding without a drop of BTC.
        Idea idea = IdeaGenerator.Generate(Fired) with { PairChange = -0.07m, BtcChange = -0.004m, Funding = 0.0006m };

        idea.Features.ShouldBe(new IdeaFeatures(true, false, true));
    }

    [Test]
    public void The_level_of_a_signal_is_that_of_its_numbers()
    {
        IdeaSignal signal = IdeaSignalGenerator.Generate(Fired) with { PairChange = -0.01m, BtcChange = -0.02m, Funding = null };

        signal.FeaturesWith(null).ShouldBe(new IdeaFeatures(false, true, false));
    }

    // Decision 2 of add-burst-and-auto-limits: a live idea keeps the count of the pairs of its minute; 25 pairs on 15 min are a burst, an idea
    // before the count was kept has none.
    [Test]
    public void The_burst_of_an_idea_is_that_of_its_count_of_pairs()
    {
        Idea idea = IdeaGenerator.Generate(Fired);

        (idea with { BurstPairs = 25 }).Features.Burst.ShouldBeTrue();
        (idea with { BurstPairs = 24 }).Features.Burst.ShouldBeFalse();
        idea.Features.Burst.ShouldBeFalse();
    }

    // Decision 2: a signal of the replay keeps no count; the pass gives the count of its minute.
    [Test]
    public void The_burst_of_a_signal_is_that_of_the_count_it_is_given()
    {
        IdeaSignal signal = IdeaSignalGenerator.Generate(Fired);

        signal.FeaturesWith(25).ShouldBe(new IdeaFeatures(false, false, false, true));
        signal.FeaturesWith(24).Burst.ShouldBeFalse();
    }
}