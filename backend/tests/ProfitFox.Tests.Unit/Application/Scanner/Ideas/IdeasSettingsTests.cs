using ProfitFox.Application.Scanner.Ideas;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Scanner.Ideas;

/// <summary>Requirement "Условия сетапов" and decision 7: thresholds common to all pairs, with a value of an interval's own.</summary>
public class IdeasSettingsTests
{
    private static readonly IdeasSettings Settings = new IdeasSettings
    {
        Thresholds = new ThresholdSettings { Candles = 6, Move = 3m, Volume = 2m, EarlyMove = 1.5m, EarlyVolume = 3m },
        ByInterval = new Dictionary<string, ThresholdOverride> { ["240"] = new ThresholdOverride { Candles = 4, Move = 2.5m } },
        StopBuffer = 1.7m
    };

    [Test]
    public void Live_rules_take_the_common_thresholds_and_the_values_an_interval_sets_of_its_own()
    {
        IdeaRuleSet rules = Settings.Live();

        rules.Thresholds.Keys.ShouldBe([Interval.FiveMinutes, Interval.FifteenMinutes, Interval.OneHour, Interval.FourHours]);
        rules.Thresholds[Interval.OneHour].ShouldBe(new SetupThresholds(6, 3m, 2m, 1.5m, 3m));
        rules.Thresholds[Interval.FourHours].ShouldBe(new SetupThresholds(4, 2.5m, 2m, 1.5m, 3m));
        (rules.StopBuffer, rules.Zones).ShouldBe((1.7m, ZoneRules.Default));
    }

    // The replay writes candidates with the floors, over as many candles as the live rules measure.
    [Test]
    public void Replay_rules_take_the_floors_with_the_candles_of_the_thresholds()
    {
        IdeaRuleSet rules = Settings.Replay();

        rules.Thresholds[Interval.FifteenMinutes].ShouldBe(new SetupThresholds(6, 2m, 1.5m, 1m, 2m));
        rules.Thresholds[Interval.FourHours].ShouldBe(new SetupThresholds(4, 2m, 1.5m, 1m, 2m));
    }
}