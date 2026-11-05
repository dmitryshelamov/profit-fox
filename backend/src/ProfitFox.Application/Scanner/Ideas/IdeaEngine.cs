using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Scanner.Ideas;

/// <summary>
///     The engine of ideas (decision 3): the live minute cycle and the replay of history feed it the closed minutes of a pair one by one,
///     without gaps, so it never sees a minute ahead. Each minute it steps the open ideas, grows the series, reads the setups of every interval
///     and makes the ideas of the setups that fire, one open idea per key. Not thread-safe: one caller at a time.
/// </summary>
public sealed class IdeaEngine(IdeaRuleSet rules, int engineVersion)
{
    private static readonly IdeaType[] Types = [IdeaType.Levels, IdeaType.TwoR, IdeaType.BeyondExtreme, IdeaType.Pullback];

    private readonly Dictionary<Instrument, Pair> _pairs = [];

    // The longest window of a setup: the last N candles of the longest interval.
    private readonly int _window = rules.Thresholds.Max(entry => entry.Value.Candles * Minutes(entry.Key));

    /// <summary>The minutes of the longest window of a setup, which a warm-up needs behind it.</summary>
    public int Window => _window;

    /// <summary>
    ///     Starts a pair over: the closed candles of each interval, the minutes up to now without gaps, which cover the candle each interval
    ///     forms, and its open ideas. Each open idea moves through the minutes since its last change (a market idea awaiting entry from the
    ///     minute after it fired), so a restart misses none of them; the ideas that moved are returned. The setups that hold on the last minute
    ///     are read as held, so only a setup that starts to hold after the warm-up fires.
    /// </summary>
    public IReadOnlyList<Idea> Warm(
        Instrument instrument,
        IReadOnlyDictionary<Interval, IReadOnlyList<IdeaCandle>> closed,
        IReadOnlyList<IdeaCandle> minutes,
        IReadOnlyList<Idea> open,
        IdeaInputs inputs)
    {
        var pair = new Pair(instrument);

        foreach (Interval interval in rules.Thresholds.Keys)
        {
            IReadOnlyList<IdeaCandle> candles = closed[interval];
            var series = new IdeaSeries(interval);
            series.Warm(candles, []);

            // A pair without a closed candle starts at the first candle its minutes cover whole; one without minutes, at its first minute.
            if (minutes.Count > 0)
            {
                DateTimeOffset from = candles.Count > 0 ? interval.CandleEnd(candles[^1].OpenTime) : interval.CandleEnd(interval.CandleOpen(minutes[0].OpenTime.AddTicks(-1)));

                foreach (IdeaCandle minute in minutes.Where(minute => minute.OpenTime >= from))
                {
                    series.Add(minute);
                }
            }

            pair.Series[interval] = series;
            pair.Holding[interval] = [];
        }

        pair.Minutes.AddRange(minutes.TakeLast(_window));
        IReadOnlyList<Idea> caught = CatchUp(open, minutes);
        pair.Open.AddRange(caught.Where(idea => idea.IsOpen));
        _pairs[instrument] = pair;

        if (minutes.Count > 0)
        {
            Read(pair, minutes[^1].OpenTime.AddMinutes(1), inputs);
        }

        return [.. caught.Where(idea => !open.Contains(idea))];
    }

    /// <summary>
    ///     Moves ideas through the minutes since their last change (a market idea awaiting entry from the minute after it fired); returns
    ///     every idea where it stands after them.
    /// </summary>
    public static IReadOnlyList<Idea> CatchUp(IReadOnlyList<Idea> ideas, IReadOnlyList<IdeaCandle> minutes)
    {
        return
        [
            .. ideas.Select(idea =>
            {
                DateTimeOffset from = idea.Track.Status == IdeaStatus.AwaitingEntry ? idea.Track.FiredAt : idea.Track.StatusAt.AddMinutes(1);
                IdeaTrack track = minutes.Where(minute => minute.OpenTime >= from).Aggregate(idea.Track, IdeaTracker.Step);

                return track == idea.Track ? idea : idea with { Track = track };
            })
        ];
    }

    /// <summary>The ideas of a pair awaiting entry or active.</summary>
    public IReadOnlyList<Idea> Open(Instrument instrument)
    {
        return [.. _pairs[instrument].Open];
    }

    public IdeaSeries Series(Instrument instrument, Interval interval)
    {
        return _pairs[instrument].Series[interval];
    }

    public int MinutesHeld(Instrument instrument)
    {
        return _pairs[instrument].Minutes.Count;
    }

    /// <param name="instrument">A pair warmed up.</param>
    /// <param name="minute">The next closed minute of the pair.</param>
    /// <param name="inputs">The spread and the percentiles of the pair now.</param>
    /// <param name="detect">Whether the pair's data is complete: without it the minute only steps the open ideas and grows the series.</param>
    public IdeaStep Step(Instrument instrument, IdeaCandle minute, IdeaInputs inputs, bool detect)
    {
        Pair pair = _pairs[instrument];
        List<Idea> changed = Advance(pair, minute);
        pair.Minutes.Add(minute);

        if (pair.Minutes.Count > _window)
        {
            pair.Minutes.RemoveAt(0);
        }

        foreach (IdeaSeries series in pair.Series.Values)
        {
            series.Add(minute);
        }

        var setups = new List<PairSetup>();
        var created = new List<Idea>();

        if (detect)
        {
            DateTimeOffset end = minute.OpenTime.AddMinutes(1);

            foreach ((Interval interval, SetupReading? reading, IReadOnlyList<SetupSignal> fired) in Read(pair, end, inputs))
            {
                setups.AddRange(pair.Holding[interval].Select(setup => new PairSetup(interval, setup)));

                if (fired.Count > 0)
                {
                    IReadOnlyList<IdeaCandle> closed = pair.Series[interval].Base(end);
                    IReadOnlyList<Zone> zones = ZoneFinder.Find(closed, reading!.Atr, rules.Zones);
                    created.AddRange(fired.SelectMany(signal => Ideas(pair, interval, signal, minute, reading.Atr, zones, inputs.Spread, end)));
                }
            }
        }

        return new IdeaStep(setups, created, changed);
    }

    /// <summary>Reads the setups of every interval on the minute that ends at <paramref name="end" />: the ones that hold and the ones that fire.</summary>
    private List<(Interval Interval, SetupReading? Reading, IReadOnlyList<SetupSignal> Fired)> Read(Pair pair, DateTimeOffset end, IdeaInputs inputs)
    {
        var readings = new List<(Interval, SetupReading?, IReadOnlyList<SetupSignal>)>();

        foreach ((Interval interval, SetupThresholds thresholds) in rules.Thresholds)
        {
            decimal? percentile = inputs.Percentiles.TryGetValue(interval, out decimal value) ? value : null;
            SetupReading? reading = SetupDetector.Detect(pair.Series[interval].Base(end), pair.Minutes, Minutes(interval), thresholds, percentile);
            readings.Add((interval, reading, SetupDetector.Fired(pair.Holding[interval], reading)));
            pair.Holding[interval] = [.. reading?.Signals.Select(signal => signal.Setup) ?? []];
        }

        return readings;
    }

    private static List<Idea> Advance(Pair pair, IdeaCandle minute)
    {
        var changed = new List<Idea>();

        for (int index = pair.Open.Count - 1; index >= 0; index--)
        {
            Idea idea = pair.Open[index];
            IdeaTrack track = IdeaTracker.Step(idea.Track, minute);

            if (track != idea.Track)
            {
                Idea moved = idea with { Track = track };
                changed.Insert(0, moved);
                pair.Open[index] = moved;

                if (!moved.IsOpen)
                {
                    pair.Open.RemoveAt(index);
                }
            }
        }

        return changed;
    }

    private List<Idea> Ideas(Pair pair, Interval interval, SetupSignal signal, IdeaCandle minute, decimal atr, IReadOnlyList<Zone> zones, decimal? spread, DateTimeOffset end)
    {
        var context = new IdeaContext(pair.Instrument.Category, minute.Close, signal.WindowLow, signal.WindowHigh, atr, zones, spread, rules.StopBuffer);
        var ideas = new List<Idea>();

        foreach (IdeaSide side in Sides(signal.Setup))
        {
            foreach (IdeaType type in Types)
            {
                var key = new IdeaKey(pair.Instrument, signal.Setup, type, interval, side);

                if (pair.Open.Any(idea => idea.Key == key) || IdeaRules.Plan(type, side, context) is not { } plan)
                {
                    continue;
                }

                var idea = new Idea(Guid.NewGuid(), pair.Instrument, signal.Setup, interval, IdeaTrack.Fired(plan, end, Minutes(interval)), signal.Move, signal.VolumeRatio, engineVersion);
                pair.Open.Add(idea);
                ideas.Add(idea);
            }
        }

        return ideas;
    }

    private static IdeaSide[] Sides(IdeaSetup setup)
    {
        return setup switch
        {
            IdeaSetup.Impulse or IdeaSetup.EarlyImpulse => [IdeaSide.Long],
            IdeaSetup.Drop or IdeaSetup.EarlyDrop => [IdeaSide.Short],
            _ => [IdeaSide.Long, IdeaSide.Short]
        };
    }

    private static int Minutes(Interval interval)
    {
        return (int)(interval.CandleEnd(DateTimeOffset.UnixEpoch) - DateTimeOffset.UnixEpoch).TotalMinutes;
    }

    private sealed class Pair(Instrument instrument)
    {
        public Instrument Instrument { get; } = instrument;

        public Dictionary<Interval, IdeaSeries> Series { get; } = [];

        public Dictionary<Interval, IReadOnlyCollection<IdeaSetup>> Holding { get; } = [];

        public List<IdeaCandle> Minutes { get; } = [];

        public List<Idea> Open { get; } = [];
    }
}