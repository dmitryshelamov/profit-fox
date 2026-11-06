using ProfitFox.Application.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Ideas;

/// <summary>Codes of setups and types in the API and in stored rows: each value has one, and the code parses back to it.</summary>
public class IdeaCodesTests
{
    [TestCase(IdeaSetup.Impulse, "impulse")]
    [TestCase(IdeaSetup.EarlyImpulse, "early_impulse")]
    [TestCase(IdeaSetup.Drop, "drop")]
    [TestCase(IdeaSetup.EarlyDrop, "early_drop")]
    [TestCase(IdeaSetup.HighVolatility, "high_volatility")]
    [TestCase(IdeaSetup.Breakout, "breakout")]
    [TestCase(IdeaSetup.FalseBreakout, "false_breakout")]
    [TestCase(IdeaSetup.Squeeze, "squeeze")]
    [TestCase(IdeaSetup.Exhaustion, "exhaustion")]
    [TestCase(IdeaSetup.RsiExtreme, "rsi_extreme")]
    [TestCase(IdeaSetup.NewExtreme, "new_extreme")]
    [TestCase(IdeaSetup.QuietVolume, "quiet_volume")]
    public void Setup_code_goes_there_and_back(IdeaSetup setup, string code)
    {
        setup.ToCode().ShouldBe(code);
        IdeaCodes.TryParseSetup(code, out IdeaSetup parsed).ShouldBeTrue();
        parsed.ShouldBe(setup);
    }

    [TestCase(IdeaType.Levels, "levels")]
    [TestCase(IdeaType.TwoR, "target_2r")]
    [TestCase(IdeaType.BeyondExtreme, "beyond_extreme")]
    [TestCase(IdeaType.Pullback, "pullback")]
    [TestCase(IdeaType.StopEntry, "stop_entry")]
    public void Type_code_goes_there_and_back(IdeaType type, string code)
    {
        type.ToCode().ShouldBe(code);
        IdeaCodes.TryParseType(code, out IdeaType parsed).ShouldBeTrue();
        parsed.ShouldBe(type);
    }
}