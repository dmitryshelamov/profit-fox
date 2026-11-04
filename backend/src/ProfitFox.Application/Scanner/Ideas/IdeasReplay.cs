using ProfitFox.Application.Backtests;
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
///     The replay of history (decisions 1, 6 and 7): the same engine of ideas over the stored minutes of each pair, day by day, with the
///     floors of the thresholds and without a spread. A pair's turn replays the days from its cursor up to its coverage, starting again a
///     horizon before the cursor so the ideas open at it close; a case closed before the cursor was written by the turn before and is left
///     out. The percentile of volatility of a day comes from the 90 days before it, and the last one goes to storage for the live ideas.
///     Between days the turn waits while the minute cycle is late.
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
    /// <summary>The longest an idea stays open: 4 candles of 4 hours waiting for an entry and 24 after it, rounded up to days.</summary>
    public static readonly TimeSpan Horizon = TimeSpan.FromDays(5);

    private static readonly TimeSpan Day = TimeSpan.FromDays(1);
    private static readonly TimeSpan PercentileWindow = TimeSpan.FromDays(90);
    private static readonly TimeSpan PaceWait = TimeSpan.FromSeconds(10);
    private static readonly int[] BucketDays = [90, 30];

    private Dictionary<Instrument, DateTimeOffset>? _cursors;

    /// <summary>Removes the cases and cursors of other versions of the rules, so a new version replays history from the start.</summary>
    public async Task CleanUpAsync(CancellationToken cancellationToken)
    {
        await cases.DeleteOtherVersionsAsync(version.Number, cancellationToken);
        await cursors.DeleteOtherVersionsAsync(version.Number, cancellationToken);
    }

    /// <summary>The statistics of every bucket over the last 90 and 30 days, by the thresholds of now.</summary>
    public async Task RecomputeBucketsAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        IReadOnlyDictionary<Interval, SetupThresholds> thresholds = settings.Live().Thresholds;
        var computed = new List<IdeaBucket>();

        foreach (int days in BucketDays)
        {
            IReadOnlyList<IdeaBucketCounts> counts = await cases.CountAsync(version.Number, now.AddDays(-days), thresholds, cancellationToken);
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
        _cursors ??= new Dictionary<Instrument, DateTimeOffset>(await cursors.GetAsync(version.Number, cancellationToken));
        DateTimeOffset now = time.GetUtcNow();
        DateTimeOffset historyStart = Ceiling(now.AddDays(-scanner.HistoryDays));
        Turn? next = market.Listed()
            .Where(pair => pair is { HistoryFrom: not null, CoveredUntil: not null })
            .Select(pair => new Turn(pair, Max(historyStart, Ceiling(pair.HistoryFrom!.Value)), Floor(pair.CoveredUntil!.Value), _cursors.TryGetValue(pair.Instrument, out DateTimeOffset at) ? at : null))
            .Where(turn => (turn.Cursor ?? turn.Start) < turn.Until)
            .MinBy(turn => turn.Cursor ?? DateTimeOffset.MinValue);

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
        DateTimeOffset from = turn.Cursor is { } cursor ? Max(turn.Start, cursor - Horizon) : turn.Start;
        var engine = new IdeaEngine(settings.Replay(), version.Number);
        FeeRates fees = backtests.Policy().DefaultFees[instrument.Category];
        Dictionary<Interval, Ratios> ratios = await WarmAsync(engine, instrument, from, cancellationToken);
        IdeaCandle? last = null;

        for (DateTimeOffset day = from; day < turn.Until; day += Day)
        {
            while (pace.IsLate)
            {
                await Task.Delay(PaceWait, time, cancellationToken);
            }

            var inputs = new IdeaInputs(null, Percentiles(ratios, day));
            IReadOnlyList<IdeaCandle> known = Minutes(await candles.GetAsync(instrument, Interval.OneMinute, day, day + Day, 1440, cancellationToken));

            // Before the first trade of a pair there is nothing to replay.
            if (last is not null || known.Count > 0)
            {
                IReadOnlyList<IdeaCandle> minutes = IdeaCandles.Minutes(known, last is null ? known[0].OpenTime : day, day + Day, last?.Close ?? known[0].Open);
                var closed = new List<IdeaCase>();

                foreach (IdeaCandle minute in minutes)
                {
                    IdeaStep step = engine.Step(instrument, minute, inputs, true);
                    closed.AddRange(step.Changed
                        .Where(idea => !idea.IsOpen && idea.Track.EnteredAt is not null && !(idea.Track.StatusAt < turn.Cursor))
                        .Select(idea => new IdeaCase(idea, CaseResult.R(idea.Track, fees)!.Value)));
                }

                foreach ((Interval interval, Ratios series) in ratios)
                {
                    series.Add(IdeaCandles.FromMinutes(interval, minutes, day, day + Day));
                }

                last = minutes[^1];
                await cases.AddAsync(closed, cancellationToken);
            }

            if (!(day + Day <= turn.Cursor))
            {
                await cursors.SetAsync(instrument, version.Number, day + Day, cancellationToken);
                _cursors![instrument] = day + Day;
            }
        }

        IReadOnlyDictionary<Interval, decimal> latest = Percentiles(ratios, turn.Until);
        await volatility.SaveAsync([.. latest.Select(entry => new IdeaVolatility(instrument, entry.Key, entry.Value))], cancellationToken);
    }

    /// <summary>Warms the engine up at the start of a turn and reads the candles the percentiles of its first day come from.</summary>
    private async Task<Dictionary<Interval, Ratios>> WarmAsync(IdeaEngine engine, Instrument instrument, DateTimeOffset from, CancellationToken cancellationToken)
    {
        var closed = new Dictionary<Interval, IReadOnlyList<IdeaCandle>>();
        var ratios = new Dictionary<Interval, Ratios>();

        foreach (Interval interval in IdeasSettings.Intervals)
        {
            TimeSpan length = interval.CandleEnd(from) - from;
            IReadOnlyList<IdeaCandle> history = await candles.GetAggregatedAsync(instrument, interval, from - PercentileWindow - (length * IdeaSeries.Capacity), from, cancellationToken);
            closed[interval] = [.. history.TakeLast(IdeaSeries.Capacity)];
            ratios[interval] = new Ratios(history);
        }

        IReadOnlyList<IdeaCandle> known = Minutes(await candles.GetAsync(instrument, Interval.OneMinute, from - Day, from, 1440, cancellationToken));
        engine.Warm(instrument, closed, known.Count == 0 ? [] : IdeaCandles.Minutes(known, known[0].OpenTime, from, known[0].Open), []);

        return ratios;
    }

    private static Dictionary<Interval, decimal> Percentiles(Dictionary<Interval, Ratios> ratios, DateTimeOffset day)
    {
        var percentiles = new Dictionary<Interval, decimal>();

        foreach ((Interval interval, Ratios series) in ratios)
        {
            if (Volatility.Percentile(series.Since(day - PercentileWindow)) is { } percentile)
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

    private sealed record Turn(ScannerPair Pair, DateTimeOffset Start, DateTimeOffset Until, DateTimeOffset? Cursor);

    /// <summary>The closed candles of an interval and the ratio of volatility after each, for one turn: 90 days and the days it replays.</summary>
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

        public IReadOnlyList<decimal> Since(DateTimeOffset from)
        {
            return [.. _ratios.Where(entry => entry.OpenTime >= from).Select(entry => entry.Ratio)];
        }

        // A ratio belongs to the candle it follows: the first one to the 14th.
        private static List<(DateTimeOffset, decimal)> Pair(IReadOnlyList<IdeaCandle> candles, IReadOnlyList<decimal> ratios)
        {
            return [.. ratios.Select((ratio, index) => (candles[candles.Count - ratios.Count + index].OpenTime, ratio))];
        }
    }
}