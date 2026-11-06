using ProfitFox.Application.Backtests;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Scanner.Ideas;

/// <summary>
///     The replay of history (decisions 1, 6 and 7): the same engine of ideas over the stored minutes of each pair, day by day, by the
///     thresholds of the live ideas and without a spread. A pair's turn replays the days from its cursor up to its coverage and goes on from
///     the ideas the cursor keeps open, warmed up as one run would stand at it, so the cases do not depend on how the days fall into turns.
///     The percentile of volatility of a day comes from the 90 days before it, and the last one goes to storage for the live ideas. Between
///     days the turn waits while the minute cycle is late. The levels go on today's price step of the pair, the only one the list of
///     instruments tells.
/// </summary>
public sealed class IdeasReplay(
    ICandleRepository candles,
    IIdeaCaseRepository cases,
    IIdeaReplayRepository cursors,
    IIdeaVolatilityRepository volatility,
    IIdeaBucketRepository buckets,
    ScannerMarket market,
    IdeasSettings settings,
    BacktestSettings backtests,
    ScannerSettings scanner,
    IdeasEngineVersion version,
    ScannerPace pace,
    TimeProvider time)
{
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);
    private static readonly TimeSpan PercentileWindow = TimeSpan.FromDays(90);
    private static readonly TimeSpan PaceWait = TimeSpan.FromSeconds(10);
    private static readonly int[] BucketDays = [90, 30];

    private Dictionary<Instrument, IdeaReplayCursor>? _cursors;

    /// <summary>Removes the cases and cursors of other versions of the rules, so a new version replays history from the start.</summary>
    public async Task CleanUpAsync(CancellationToken cancellationToken)
    {
        await cases.DeleteOtherVersionsAsync(version.Number, cancellationToken);
        await cursors.DeleteOtherVersionsAsync(version.Number, cancellationToken);
    }

    /// <summary>The statistics of every bucket over the last 90 and 30 days.</summary>
    public async Task RecomputeBucketsAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        var computed = new List<IdeaBucket>();

        foreach (int days in BucketDays)
        {
            IReadOnlyList<IdeaBucketCounts> counts = await cases.CountAsync(version.Number, now.AddDays(-days), cancellationToken);
            computed.AddRange(counts.Select(count => new IdeaBucket(
                count.Key,
                days,
                BucketStatisticsCalculator.Compute(count.Targets, count.Stops, count.Expired, count.AverageRiskReward, count.TotalR))));
        }

        await buckets.ReplaceAsync(version.Number, computed, cancellationToken);
    }

    /// <summary>Replays the turn of the pair furthest behind; <c>false</c> when every pair is replayed up to its coverage.</summary>
    public async Task<bool> StepAsync(CancellationToken cancellationToken)
    {
        _cursors ??= new Dictionary<Instrument, IdeaReplayCursor>(await cursors.GetAsync(version.Number, cancellationToken));
        DateTimeOffset now = time.GetUtcNow();
        DateTimeOffset historyStart = Ceiling(now.AddDays(-scanner.HistoryDays));
        Turn? next = market.Listed()
            // A pair waits for its price step too: a day replayed without ideas would never be replayed again.
            .Where(pair => pair is { HistoryFrom: not null, CoveredUntil: not null, TickSize: not null })
            .Select(pair =>
            {
                DateTimeOffset start = Max(historyStart, Ceiling(pair.HistoryFrom!.Value));

                // A cursor older than the history window starts the pair over: the days after it may hold no minutes any more.
                IdeaReplayCursor? cursor = _cursors.GetValueOrDefault(pair.Instrument) is { } stored && stored.ReplayedUntil >= start ? stored : null;

                return new Turn(pair, start, Floor(pair.CoveredUntil!.Value), cursor);
            })
            .Where(turn => turn.From < turn.Until)
            .MinBy(turn => turn.Cursor?.ReplayedUntil ?? DateTimeOffset.MinValue);

        if (next is null)
        {
            return false;
        }

        await ReplayAsync(next, cancellationToken);

        return true;
    }

    private async Task ReplayAsync(Turn turn, CancellationToken cancellationToken)
    {
        Instrument instrument = turn.Pair.Instrument;
        var engine = new IdeaEngine(settings.Rules(), version.Number);
        FeeRates fees = backtests.Policy().DefaultFees[instrument.Category];
        (Dictionary<Interval, Ratios> ratios, IdeaCandle? last) = await WarmAsync(engine, instrument, turn, cancellationToken);
        var closed = new List<IdeaCase>();

        for (DateTimeOffset day = turn.From; day < turn.Until; day += Day)
        {
            while (pace.IsLate)
            {
                await Task.Delay(PaceWait, time, cancellationToken);
            }

            var inputs = new IdeaInputs(null, Percentiles(ratios, day), turn.Pair.TickSize);
            IReadOnlyList<IdeaCandle> known = Minutes(await candles.GetAsync(instrument, Interval.OneMinute, day, day + Day, 1440, cancellationToken));

            // Before the first trade of a pair there is nothing to replay; after it every minute of the day is fed, the ones without trades flat.
            IReadOnlyList<IdeaCandle> minutes = IdeaCandles.Minutes(known, day, day + Day, last?.Close);

            if (minutes.Count > 0)
            {

                foreach (IdeaCandle minute in minutes)
                {
                    closed.AddRange(Cases(engine.Step(instrument, minute, inputs, true).Changed, fees));
                }

                foreach ((Interval interval, Ratios series) in ratios)
                {
                    series.Add(IdeaCandles.FromMinutes(interval, minutes, day, day + Day));
                }

                last = minutes[^1];
                await cases.AddAsync(closed, cancellationToken);
                closed = [];
            }

            var cursor = new IdeaReplayCursor(day + Day, engine.Open(instrument));
            await cursors.SetAsync(instrument, version.Number, cursor, cancellationToken);
            _cursors![instrument] = cursor;
        }

        IReadOnlyDictionary<Interval, decimal> latest = Percentiles(ratios, turn.Until);
        await volatility.SaveAsync([.. latest.Select(entry => new IdeaVolatility(instrument, entry.Key, entry.Value))], cancellationToken);
    }

    /// <summary>
    ///     Warms the engine up at the start of a turn as one run would stand there: the closed candles, the day of minutes before, flat from
    ///     the close before it where it had no trades, the open ideas of the cursor and the percentiles of that day. Also reads the candles the
    ///     percentiles come from.
    /// </summary>
    private async Task<(Dictionary<Interval, Ratios> Ratios, IdeaCandle? Last)> WarmAsync(
        IdeaEngine engine,
        Instrument instrument,
        Turn turn,
        CancellationToken cancellationToken)
    {
        DateTimeOffset from = turn.From;
        var closed = new Dictionary<Interval, IReadOnlyList<IdeaCandle>>();
        var ratios = new Dictionary<Interval, Ratios>();
        var histories = new Dictionary<Interval, IReadOnlyList<IdeaCandle>>();

        foreach (Interval interval in IdeasSettings.Intervals)
        {
            TimeSpan length = interval.CandleEnd(from) - from;
            IReadOnlyList<IdeaCandle> history = await candles.GetAggregatedAsync(
                instrument,
                interval,
                from - Day - PercentileWindow - (length * IdeaSeries.Capacity),
                from,
                cancellationToken);
            histories[interval] = history;
            closed[interval] = [.. history.TakeLast(IdeaSeries.Capacity)];
            ratios[interval] = new Ratios(history);
        }

        // At least a day of minutes, and whole days of the longest window of a setup.
        TimeSpan span = Day * Math.Max(1, (int)Math.Ceiling(engine.Window / Day.TotalMinutes));
        IReadOnlyList<IdeaCandle> known = Minutes(await candles.GetAsync(instrument, Interval.OneMinute, from - span, from, (int)span.TotalMinutes, cancellationToken));
        decimal? closeBefore = histories[Interval.FiveMinutes].LastOrDefault(candle => Interval.FiveMinutes.CandleEnd(candle.OpenTime) <= from - span)?.Close;
        IReadOnlyList<IdeaCandle> minutes = IdeaCandles.Minutes(known, from - span, from, closeBefore);
        // The cursor keeps its ideas where a run stood at it, so the warm-up moves none of them.
        engine.Warm(instrument, closed, minutes, turn.Cursor?.Open ?? [], new IdeaInputs(null, Percentiles(ratios, from - Day), turn.Pair.TickSize));

        return (ratios, minutes.Count > 0 ? minutes[^1] : null);
    }

    private static List<IdeaCase> Cases(IEnumerable<Idea> changed, FeeRates fees)
    {
        return [.. changed.Where(idea => !idea.IsOpen && idea.Track.EnteredAt is not null).Select(idea => new IdeaCase(idea, CaseResult.R(idea.Track, fees)!.Value))];
    }

    /// <summary>The percentile of each interval on a day: of the ratios of the 90 days before it.</summary>
    private static Dictionary<Interval, decimal> Percentiles(Dictionary<Interval, Ratios> ratios, DateTimeOffset day)
    {
        var percentiles = new Dictionary<Interval, decimal>();

        foreach ((Interval interval, Ratios series) in ratios)
        {
            if (Volatility.Percentile(series.Between(day - PercentileWindow, day), 0.9m) is { } percentile)
            {
                percentiles[interval] = percentile;
            }
        }

        return percentiles;
    }

    private static IReadOnlyList<IdeaCandle> Minutes(IReadOnlyList<Candle> stored)
    {
        return [.. stored.Select(candle => new IdeaCandle(candle.OpenTime, candle.Open.Value, candle.High.Value, candle.Low.Value, candle.Close.Value, candle.Turnover.Value))];
    }

    private static DateTimeOffset Floor(DateTimeOffset time)
    {
        return Interval.OneDay.CandleOpen(time);
    }

    private static DateTimeOffset Ceiling(DateTimeOffset time)
    {
        DateTimeOffset floor = Floor(time);

        return floor == time ? floor : floor + Day;
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right)
    {
        return left > right ? left : right;
    }

    private sealed record Turn(ScannerPair Pair, DateTimeOffset Start, DateTimeOffset Until, IdeaReplayCursor? Cursor)
    {
        public DateTimeOffset From => Cursor?.ReplayedUntil ?? Start;
    }

    /// <summary>The closed candles of an interval and the ratio of volatility after each, for one turn: 91 days and the days it replays.</summary>
    private sealed class Ratios(IReadOnlyList<IdeaCandle> history)
    {
        private readonly List<IdeaCandle> _candles = [.. history];
        private readonly List<(DateTimeOffset OpenTime, decimal Ratio)> _ratios = Pair(history, Volatility.Ratios(history));

        public void Add(IReadOnlyList<IdeaCandle> candles)
        {
            _candles.AddRange(candles);
            IdeaCandle[] tail = [.. _candles.TakeLast(candles.Count + IdeaSeries.Capacity - 1)];
            IReadOnlyList<decimal> ratios = Volatility.Ratios(tail);
            _ratios.AddRange(Pair(tail, ratios).TakeLast(Math.Min(candles.Count, ratios.Count)));
        }

        public IReadOnlyList<decimal> Between(DateTimeOffset from, DateTimeOffset to)
        {
            return [.. _ratios.Where(entry => entry.OpenTime >= from && entry.OpenTime < to).Select(entry => entry.Ratio)];
        }

        // A ratio belongs to the candle it follows: the first one to the 14th.
        private static List<(DateTimeOffset, decimal)> Pair(IReadOnlyList<IdeaCandle> candles, IReadOnlyList<decimal> ratios)
        {
            return [.. ratios.Select((ratio, index) => (candles[candles.Count - ratios.Count + index].OpenTime, ratio))];
        }
    }
}