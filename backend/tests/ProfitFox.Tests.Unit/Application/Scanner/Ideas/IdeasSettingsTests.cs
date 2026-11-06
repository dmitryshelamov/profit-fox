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
        Thresholds = new ThresholdSettings
        {
            Candles = 6,
            Move = 3m,
            Volume = 2m,
            EarlyMove = 1.5m,
            EarlyVolume = 3m,
            BreakoutMargin = 0.1m,
            BreakoutVolume = 1.5m,
            SweepMargin = 0.2m,
            SqueezeCandles = 20,
            ClimaxVolume = 4m,
            Wick = 1m,
            QuietVolume = 3m,
            QuietBody = 0.3m,
            Overbought = 70m,
            Oversold = 30m
        },
        ByInterval = new Dictionary<string, ThresholdOverride>
        {
            ["240"] = new ThresholdOverride { Candles = 4, Move = 2.5m },
            ["5"] = new ThresholdOverride
            {
                BreakoutMargin = 0.2m,
                BreakoutVolume = 2m,
                SweepMargin = 0.3m,
                SqueezeCandles = 30,
                ClimaxVolume = 5m,
                Wick = 1.5m,
                QuietVolume = 4m,
                QuietBody = 0.2m,
                Overbought = 80m,
                Oversold = 20m
            }
        },
        StopBuffer = 1.7m
    };

    [Test]
    public void Live_rules_take_the_common_thresholds_and_the_values_an_interval_sets_of_its_own()
    {
        IdeaRuleSet rules = Settings.Rules();

        rules.Thresholds.Keys.ShouldBe([Interval.FiveMinutes, Interval.FifteenMinutes, Interval.OneHour, Interval.FourHours]);
        rules.Thresholds[Interval.OneHour].ShouldBe(new SetupThresholds(6, 3m, 2m, 1.5m, 3m, 0.1m, 1.5m, 0.2m, 20, 4m, 1m, 3m, 0.3m, 70m, 30m));
        rules.Thresholds[Interval.FourHours].ShouldBe(new SetupThresholds(4, 2.5m, 2m, 1.5m, 3m, 0.1m, 1.5m, 0.2m, 20, 4m, 1m, 3m, 0.3m, 70m, 30m));
        (rules.StopBuffer, rules.Zones).ShouldBe((1.7m, ZoneRules.Default));
    }

    // Requirement "Пороги свечных сетапов": the thresholds of the candle setups are common too, with values of an interval's own.
    [Test]
    public void Thresholds_of_the_candle_setups_take_the_values_an_interval_sets_of_its_own()
    {
        Settings.Rules().Thresholds[Interval.FiveMinutes].ShouldBe(new SetupThresholds(6, 3m, 2m, 1.5m, 3m, 0.2m, 2m, 0.3m, 30, 5m, 1.5m, 4m, 0.2m, 80m, 20m));
    }
}