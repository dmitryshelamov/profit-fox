using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Services.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>Requirement "Типы идей по сетапу": a setup gives ideas of its own types only.</summary>
public class SetupTypesTests
{
    [TestCase(IdeaSetup.Impulse)]
    [TestCase(IdeaSetup.EarlyImpulse)]
    [TestCase(IdeaSetup.Drop)]
    [TestCase(IdeaSetup.EarlyDrop)]
    [TestCase(IdeaSetup.HighVolatility)]
    [TestCase(IdeaSetup.Breakout)]
    [TestCase(IdeaSetup.FalseBreakout)]
    [TestCase(IdeaSetup.NewExtreme)]
    public void Setup_gives_the_four_types_at_the_market_and_the_pullback(IdeaSetup setup)
    {
        SetupTypes.Of(setup).ShouldBe([IdeaType.Levels, IdeaType.TwoR, IdeaType.BeyondExtreme, IdeaType.Pullback]);
    }

    [TestCase(IdeaSetup.Exhaustion)]
    [TestCase(IdeaSetup.RsiExtreme)]
    public void Setup_against_the_move_gives_no_pullback(IdeaSetup setup)
    {
        SetupTypes.Of(setup).ShouldBe([IdeaType.Levels, IdeaType.TwoR, IdeaType.BeyondExtreme]);
    }

    // Scenario "Сжатие": only «Стоп-вход».
    [TestCase(IdeaSetup.Squeeze)]
    [TestCase(IdeaSetup.QuietVolume)]
    public void Setup_of_a_range_gives_only_the_stop_entry(IdeaSetup setup)
    {
        SetupTypes.Of(setup).ShouldBe([IdeaType.StopEntry]);
    }
}