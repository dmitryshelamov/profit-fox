using ProfitFox.Application.Scanner.Ideas;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Fixtures.Ideas;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Scanner.Ideas;

/// <summary>
///     Decision 3: the engine takes the minutes of a pair one by one and makes, steps and closes its ideas. It runs on the 15 minute sets of
///     <see cref="IdeaFixtures.Setup" /> with only the 15 minute interval; two dips of the flat part give a support at 98.0–98.2.
/// </summary>
public class IdeaEngineTests
{
    private static readonly Instrument Wif = new Instrument(MarketCategory.Linear, "WIFUSDT");
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-02-29T23:30:00Z");
    private static readonly DateTimeOffset Three = DateTimeOffset.Parse("2024-03-01T03:00:00Z");
    private static readonly DateTimeOffset Five = DateTimeOffset.Parse("2024-03-01T05:00:00Z");

    private static readonly IdeaRuleSet Rules = new IdeaRuleSet(
        new Dictionary<Interval, SetupThresholds> { [Interval.FifteenMinutes] = new SetupThresholds(6, 3m, 2m, 1.5m, 3m) },
        1.5m,
        ZoneRules.Default);

    [Test]
    public void Impulse_that_fires_makes_its_ideas_of_every_type_its_levels_allow()
    {
        IReadOnlyList<IdeaStep> steps = Run(Impulse(), Wif, Three);

        IdeaStep fired = steps.Single(step => step.Created.Count > 0);
        fired.Setups.ShouldBe([new PairSetup(Interval.FifteenMinutes, IdeaSetup.Impulse)]);
        fired.Created.Select(idea => (idea.Setup, idea.Track.Plan.Type, idea.Track.Plan.Side, idea.Interval)).ShouldBe(
        [
            (IdeaSetup.Impulse, IdeaType.TwoR, IdeaSide.Long, Interval.FifteenMinutes),
            (IdeaSetup.Impulse, IdeaType.BeyondExtreme, IdeaSide.Long, Interval.FifteenMinutes),
            (IdeaSetup.Impulse, IdeaType.Pullback, IdeaSide.Long, Interval.FifteenMinutes)
        ]);
        fired.Created.ShouldAllBe(idea => idea.Track.Status == IdeaStatus.AwaitingEntry && idea.EngineVersion == 1 && idea.Move >= 3m);
    }

    // The ideas at the market enter on the next minute: the step reports them moved.
    [Test]
    public void Market_ideas_enter_on_the_minute_after_they_fired()
    {
        IReadOnlyList<IdeaStep> steps = Run(Impulse(), Wif, Three);
        int fired = steps.ToList().FindIndex(step => step.Created.Count > 0);

        steps[fired + 1].Changed.Select(idea => (idea.Track.Plan.Type, idea.Track.Status)).ShouldBe(
            [(IdeaType.TwoR, IdeaStatus.Active), (IdeaType.BeyondExtreme, IdeaStatus.Active)]);
    }

    // Scenario "Повторный импульс": a dip at 05:03 breaks the impulse for a minute; it fires again while its ideas are open.
    [Test]
    public void Setup_that_fires_again_makes_no_second_idea_of_an_open_key()
    {
        IReadOnlyList<IdeaStep> steps = Run(Dipped(Impulse()), Wif, Three);

        int[] impulse = [.. steps.Select((step, index) => step.Setups.Any(setup => setup.Setup == IdeaSetup.Impulse) ? index : -1).Where(index => index >= 0)];
        impulse.Zip(impulse.Skip(1)).Count(pair => pair.Second > pair.First + 1).ShouldBe(1);
        steps.SelectMany(step => step.Created).Count(idea => idea.Track.Plan.Type == IdeaType.TwoR).ShouldBe(1);
    }

    // Scenario "Падение на spot".
    [Test]
    public void Drop_on_spot_is_a_setup_without_ideas()
    {
        var eth = new Instrument(MarketCategory.Spot, "ETHUSDT");

        IReadOnlyList<IdeaStep> steps = Run(IdeaFixtures.Setup("drop"), eth, Three);

        steps.ShouldContain(step => step.Setups.Contains(new PairSetup(Interval.FifteenMinutes, IdeaSetup.Drop)));
        steps.SelectMany(step => step.Created).ShouldBeEmpty();
    }

    // After a warm-up the setups that already hold are taken as they are: only a setup that starts to hold fires.
    [Test]
    public void Setup_that_holds_at_the_warm_up_does_not_fire()
    {
        IReadOnlyList<IdeaStep> steps = Run(Impulse(), Wif, Five);

        steps[0].Setups.ShouldBe([new PairSetup(Interval.FifteenMinutes, IdeaSetup.Impulse)]);
        steps.SelectMany(step => step.Created).ShouldBeEmpty();
    }

    // Requirement "Сетапы и идеи в минутном цикле": a pair with incomplete data gets no setups, while its open ideas still move.
    [Test]
    public void Minute_without_detection_gives_no_setups_but_steps_the_open_ideas()
    {
        Idea open = IdeaGenerator.Generate(Five.AddMinutes(-1), instrument: Wif);
        var engine = new IdeaEngine(Rules, IdeasEngineVersion.Current);
        IReadOnlyList<IdeaCandle> minutes = Impulse();
        engine.Warm(Wif, Closed(minutes, Five), [.. minutes.Where(minute => minute.OpenTime < Five)], [open]);

        IdeaStep step = engine.Step(Wif, minutes.Single(minute => minute.OpenTime == Five), IdeaInputs.None, false);

        step.Setups.ShouldBeEmpty();
        step.Created.ShouldBeEmpty();
        step.Changed.Single().Track.Status.ShouldBe(IdeaStatus.Active);
    }

    // A pair listed lately has no closed candle yet: its series starts at the first candle its minutes cover whole.
    [Test]
    public void New_pair_starts_its_series_at_the_first_whole_candle_of_its_minutes()
    {
        var engine = new IdeaEngine(Rules, IdeasEngineVersion.Current);
        IdeaCandle[] minutes = [.. Impulse().Where(minute => minute.OpenTime >= Start.AddMinutes(7) && minute.OpenTime < Three)];
        engine.Warm(Wif, new Dictionary<Interval, IReadOnlyList<IdeaCandle>> { [Interval.FifteenMinutes] = [] }, minutes, []);

        engine.Series(Wif, Interval.FifteenMinutes).Closed.ShouldBe(IdeaCandles.FromMinutes(Interval.FifteenMinutes, minutes, Start.AddMinutes(15), Three));
    }

    // A closed idea gives its key back: the impulse makes its idea of 2R although one was open at the warm-up.
    [Test]
    public void Idea_that_closes_leaves_its_key_to_the_next_setup()
    {
        Idea generated = IdeaGenerator.Generate(Three.AddMinutes(-1), instrument: Wif);
        Idea open = generated with { Track = generated.Track with { Plan = generated.Track.Plan with { Stop = 101m } } };
        var engine = new IdeaEngine(Rules, IdeasEngineVersion.Current);
        IReadOnlyList<IdeaCandle> minutes = Impulse();
        engine.Warm(Wif, Closed(minutes, Three), [.. minutes.Where(minute => minute.OpenTime < Three)], [open]);

        IdeaStep[] steps = [.. minutes.Where(minute => minute.OpenTime >= Three).Select(minute => engine.Step(Wif, minute, IdeaInputs.None, true))];

        steps[0].Changed.Single().Track.Status.ShouldBe(IdeaStatus.Expired);
        steps.SelectMany(step => step.Created).ShouldContain(created => created.Track.Plan.Type == IdeaType.TwoR);
    }

    // High volatility gives ideas of both sides.
    [Test]
    public void High_volatility_above_the_percentile_of_the_pair_gives_long_and_short_ideas()
    {
        var engine = new IdeaEngine(Rules, IdeasEngineVersion.Current);
        IReadOnlyList<IdeaCandle> minutes = Impulse();
        engine.Warm(Wif, Closed(minutes, Three), [.. minutes.Where(minute => minute.OpenTime < Three)], []);
        var inputs = new IdeaInputs(0.01m, new Dictionary<Interval, decimal> { [Interval.FifteenMinutes] = 0.0186m });

        IdeaStep[] steps = [.. minutes.Where(minute => minute.OpenTime >= Three).Select(minute => engine.Step(Wif, minute, inputs, true))];

        steps.ShouldContain(step => step.Setups.Contains(new PairSetup(Interval.FifteenMinutes, IdeaSetup.HighVolatility)));
        steps.SelectMany(step => step.Created).Where(idea => idea.Setup == IdeaSetup.HighVolatility).Select(idea => idea.Track.Plan.Side).Distinct()
            .ShouldBe([IdeaSide.Long, IdeaSide.Short], ignoreOrder: true);
    }

    // The engine keeps the minutes of the longest window of a setup: 6 candles of 15 minutes.
    [Test]
    public void Engine_keeps_the_minutes_of_the_longest_window_only()
    {
        var engine = new IdeaEngine(Rules, IdeasEngineVersion.Current);
        IReadOnlyList<IdeaCandle> minutes = Impulse();
        engine.Warm(Wif, Closed(minutes, Three), [.. minutes.Where(minute => minute.OpenTime < Three)], []);

        foreach (IdeaCandle minute in minutes.Where(minute => minute.OpenTime >= Three))
        {
            engine.Step(Wif, minute, IdeaInputs.None, true);
        }

        engine.MinutesHeld(Wif).ShouldBe(90);
    }

    // The replay starts a pair at the first day of its history, before which it has no minutes at all.
    [Test]
    public void Pair_without_minutes_before_its_history_starts_with_its_first_minute()
    {
        var engine = new IdeaEngine(Rules, IdeasEngineVersion.Current);
        engine.Warm(Wif, new Dictionary<Interval, IReadOnlyList<IdeaCandle>> { [Interval.FifteenMinutes] = [] }, [], []);

        foreach (IdeaCandle minute in Impulse().Where(minute => minute.OpenTime < Three))
        {
            engine.Step(Wif, minute, IdeaInputs.None, true);
        }

        engine.Series(Wif, Interval.FifteenMinutes).Closed.ShouldBe(IdeaCandles.FromMinutes(Interval.FifteenMinutes, Impulse(), Start, Three));
    }

    // The impulse set with lows of 98.0 and 98.2 at the first minutes of 01:00 and 02:00.
    private static IReadOnlyList<IdeaCandle> Impulse()
    {
        var one = DateTimeOffset.Parse("2024-03-01T01:00:00Z");

        return
        [
            .. IdeaFixtures.Setup("impulse").Select(minute => minute.OpenTime == one ? minute with { Low = 98.0m }
                : minute.OpenTime == one.AddHours(1) ? minute with { Low = 98.2m } : minute)
        ];
    }

    // 05:03 falls by 2.5 and 05:04 rises back by 3.2.
    private static IReadOnlyList<IdeaCandle> Dipped(IReadOnlyList<IdeaCandle> minutes)
    {
        DateTimeOffset dip = Five.AddMinutes(3);
        decimal before = minutes.Single(minute => minute.OpenTime == dip).Open;

        return
        [
            .. minutes.Select(minute => minute.OpenTime == dip ? minute with { High = before, Low = before - 2.6m, Close = before - 2.5m }
                : minute.OpenTime == dip.AddMinutes(1) ? minute with { Open = before - 2.5m, High = before + 0.7m, Low = before - 2.5m, Close = before + 0.7m }
                : minute.OpenTime > dip.AddMinutes(1) ? minute with { Open = minute.Open + 0.7m - 0.22m, High = minute.High + 0.48m, Low = minute.Low + 0.48m, Close = minute.Close + 0.48m }
                : minute)
        ];
    }

    private static IReadOnlyList<IdeaStep> Run(IReadOnlyList<IdeaCandle> minutes, Instrument instrument, DateTimeOffset warmAt)
    {
        var engine = new IdeaEngine(Rules, IdeasEngineVersion.Current);
        engine.Warm(instrument, Closed(minutes, warmAt), [.. minutes.Where(minute => minute.OpenTime < warmAt)], []);

        return [.. minutes.Where(minute => minute.OpenTime >= warmAt).Select(minute => engine.Step(instrument, minute, IdeaInputs.None, true))];
    }

    private static Dictionary<Interval, IReadOnlyList<IdeaCandle>> Closed(IReadOnlyList<IdeaCandle> minutes, DateTimeOffset at)
    {
        return new Dictionary<Interval, IReadOnlyList<IdeaCandle>>
        {
            [Interval.FifteenMinutes] = IdeaCandles.FromMinutes(Interval.FifteenMinutes, minutes, Start, Interval.FifteenMinutes.CandleOpen(at))
        };
    }
}