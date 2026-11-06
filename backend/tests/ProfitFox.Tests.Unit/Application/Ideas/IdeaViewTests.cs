using ProfitFox.Application.Ideas;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Ideas;

/// <summary>Requirement "Безубыток идеи": an idea breaks even by the fees of its category.</summary>
public class IdeaViewTests
{
    private static readonly DateTimeOffset Noon = DateTimeOffset.Parse("2024-03-10T12:00:00Z");
    private static readonly Dictionary<MarketCategory, FeeRates> Fees = new Dictionary<MarketCategory, FeeRates> { [MarketCategory.Linear] = new FeeRates(0.02m, 0.055m) };

    // Scenario "Безубыток с комиссиями": linear long 100 → 98 / 104 with the taker 0.055 %.
    [Test]
    public void Idea_breaks_even_by_the_fees_of_its_category()
    {
        IdeaView view = IdeaView.Of([Idea(new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, 104m))], [], Fees).Single();

        view.Shares.BreakEven.ShouldBe(0.021089m / (0.038878m + 0.021089m));
    }

    // Scenario "Цель не окупает комиссии": a target 0.1 % away pays less than the two takers.
    [Test]
    public void Idea_whose_target_does_not_cover_the_fees_has_no_break_even()
    {
        IdeaView view = IdeaView.Of([Idea(new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 99.95m, 100.1m))], [], Fees).Single();

        view.Shares.BreakEven.ShouldBeNull();
    }

    private static Idea Idea(IdeaPlan plan)
    {
        Idea idea = IdeaGenerator.Generate(Noon);

        return idea with { Track = idea.Track with { Plan = plan } };
    }
}