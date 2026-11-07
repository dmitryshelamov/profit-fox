using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Scanner.Ideas;

/// <summary>
///     The replay of history (decisions 1 and 4 of tune-idea-rules): the same engine of ideas over the stored minutes of each pair, day by day,
///     by the thresholds of the live ideas and without a spread, giving every signal with its path. A pair's turn replays the days from its
///     cursor up to its coverage and goes on from the paths the cursor keeps open, warmed up as one run would stand at it, so the signals do not
///     depend on how the days fall into turns. The signals whose paths ended on a day are written at its end; the ones still open at the end of
///     the turn are written as far as they go, and written again as they go on.
///     The percentiles of volatility of a day come from the 90 days before it, and the last ones go to storage for the live ideas. Between
///     days the turn waits while the minute cycle is late. The levels go on today's price step of the pair, the only one the list of
///     instruments tells.
/// </summary>
public sealed class IdeasReplay(
    ICandleRepository candles,
    IIdeaSignalRepository signals,
    IIdeaReplayRepository cursors,
    IIdeaVolatilityRepository volatility,
    IFundingRateRepository funding,
    ScannerMarket market,
    IdeasSettings settings,
    ScannerSettings scanner,
    IdeasEngineVersion version,
    ScannerPace pace,
    TimeProvider time)
{
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);
    private static readonly TimeSpan PercentileWindow = TimeSpan.FromDays(90);
    private static readonly TimeSpan PaceWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan BtcSpan = TimeSpan.FromMinutes(240);
    private static readonly TimeSpan FundingBefore = TimeSpan.FromDays(7);
    private static readonly Instrument Btc = new Instrument(MarketCategory.Linear, "BTCUSDT");

    // A settlement every hour at the most over the 8 days of funding a day reads.
    private const int FundingRead = 8 * 24;

    private Dictionary<Instrument, IdeaReplayCursor>? _cursors;

    /// <summary>
    ///     Removes an hour of the signals of other versions of the rules, and their cursors once no signal is left, so a new version replays
    ///     history from the start; <c>true</c> while some are left.
    /// </summary>
    public async Task<bool> CleanUpAsync(CancellationToken cancellationToken)
    {
        if (await signals.DeleteOtherVersionsAsync(version.Number, cancellationToken) > 0)
        {
            return true;
        }

        await cursors.DeleteOtherVersionsAsync(version.Number, cancellationToken);

        return false;
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
                IdeaReplayCursor? stored = _cursors.GetValueOrDefault(pair.Instrument);
                IdeaReplayCursor? cursor = stored?.ReplayedUntil >= start ? stored : null;

                return new Turn(pair, start, Floor(pair.CoveredUntil!.Value), cursor, stored is not null && cursor is null);
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
        // The signals a pair started over had stored stay out: a path of them left unfinished would leave the later signals of its key undecided.
        if (turn.StartsOver)
        {
            await signals.DeleteAsync(instrument, version.Number, cancellationToken);
        }

        var engine = new IdeaEngine(settings.Rules(), version.Number, true);
        (Dictionary<Interval, Ratios> ratios, IdeaCandle? last) = await WarmAsync(engine, instrument, turn, cancellationToken);
        List<IdeaSignal> open = [.. turn.Cursor?.Open ?? []];
        var ended = new List<IdeaSignal>();

        for (DateTimeOffset day = turn.From; day < turn.Until; day += Day)
        {
            while (pace.IsLate)
            {
                await Task.Delay(PaceWait, time, cancellationToken);
            }

            var inputs = new IdeaInputs(null, Percentiles(ratios, day), turn.Pair.TickSize);
            IReadOnlyList<IdeaCandle> known = Minutes(await candles.GetAsync(instrument, Interval.OneMinute, day, day + Day, 1440, cancellationToken));
            Market dayMarket = await MarketAsync(instrument, day, cancellationToken);

            // Before the first trade of a pair there is nothing to replay; after it every minute of the day is fed, the ones without trades flat.
            IReadOnlyList<IdeaCandle> minutes = IdeaCandles.Minutes(known, day, day + Day, last?.Close);

            if (minutes.Count > 0)
            {
                foreach (IdeaCandle minute in minutes)
                {
                    // A signal goes on from the minute after it fired.
                    Advance(open, ended, minute);
                    open.AddRange(engine.Step(instrument, minute, dayMarket.Inputs(inputs, minute.OpenTime), true).Signals);
                }

                foreach ((Interval interval, Ratios series) in ratios)
                {
                    series.Add(IdeaCandles.FromMinutes(interval, minutes, day, day + Day));
                }

                last = minutes[^1];
                await signals.UpsertAsync(ended, cancellationToken);
                ended = [];
            }

            var cursor = new IdeaReplayCursor(day + Day, [.. open]);
            await cursors.SetAsync(instrument, version.Number, cursor, cancellationToken);
            _cursors![instrument] = cursor;
        }

        await signals.UpsertAsync(open, cancellationToken);
        IReadOnlyDictionary<Interval, VolatilityPercentiles> latest = Percentiles(ratios, turn.Until);
        await volatility.SaveAsync([.. latest.Select(entry => new IdeaVolatility(instrument, entry.Key, entry.Value.High!.Value, entry.Value.Low))], cancellationToken);
    }

    /// <summary>
    ///     Warms the engine up at the start of a turn as one run would stand there: the closed candles, the day of minutes before, flat from
    ///     the close before it where it had no trades, and the percentiles of that day. Also reads the candles the percentiles come from.
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
        engine.Warm(instrument, closed, minutes, [], new IdeaInputs(null, Percentiles(ratios, from - Day), turn.Pair.TickSize));

        return (ratios, minutes.Count > 0 ? minutes[^1] : null);
    }

    /// <summary>
    ///     The market of a day the features of its signals are measured by (decision 5 of add-market-idea-features): the closes of the minutes of
    ///     <c>linear</c> / <c>BTCUSDT</c> from 240 minutes before the day to its end, and the funding settlements of a linear pair from 7 days
    ///     before it, an interval of funding being 8 hours at the most.
    /// </summary>
    private async Task<Market> MarketAsync(Instrument instrument, DateTimeOffset day, CancellationToken cancellationToken)
    {
        IReadOnlyList<Candle> btc = await candles.GetAsync(Btc, Interval.OneMinute, day - BtcSpan, day + Day, (int)(BtcSpan + Day).TotalMinutes, cancellationToken);
        IReadOnlyList<FundingRatePoint> rates = instrument.Category == MarketCategory.Linear
            ? await funding.GetAsync(instrument, day - FundingBefore, day + Day, FundingRead, cancellationToken)
            : [];

        return new Market(btc.ToDictionary(candle => candle.OpenTime, candle => candle.Close.Value), rates);
    }

    // Steps the open paths by the minute; the ones that end move to the ended.
    private static void Advance(List<IdeaSignal> open, List<IdeaSignal> ended, IdeaCandle minute)
    {
        for (int index = open.Count - 1; index >= 0; index--)
        {
            IdeaSignal signal = open[index];
            SignalPath path = SignalPathTracker.Step(signal.Path, minute);

            if (!path.IsOpen)
            {
                ended.Add(signal with { Path = path });
                open.RemoveAt(index);
            }
            else
            {
                open[index] = signal with { Path = path };
            }
        }
    }

    /// <summary>The 10th and 90th percentiles of each interval on a day: of the same ratios of the 90 days before it.</summary>
    private static Dictionary<Interval, VolatilityPercentiles> Percentiles(Dictionary<Interval, Ratios> ratios, DateTimeOffset day)
    {
        var percentiles = new Dictionary<Interval, VolatilityPercentiles>();

        foreach ((Interval interval, Ratios series) in ratios)
        {
            IReadOnlyList<decimal> between = series.Between(day - PercentileWindow, day);

            // Both need the same 100 ratios: a day has both or neither.
            if (Volatility.Percentile(between, 0.9m) is { } high)
            {
                percentiles[interval] = new VolatilityPercentiles(Volatility.Percentile(between, 0.1m), high);
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

    private sealed record Turn(ScannerPair Pair, DateTimeOffset Start, DateTimeOffset Until, IdeaReplayCursor? Cursor, bool StartsOver)
    {
        public DateTimeOffset From => Cursor?.ReplayedUntil ?? Start;
    }

    /// <summary>The closes of BTC and the funding settlements of a day, oldest first, which the minutes of the day step through in order.</summary>
    private sealed class Market(Dictionary<DateTimeOffset, decimal> btc, IReadOnlyList<FundingRatePoint> rates)
    {
        private int _settled;

        /// <summary>The inputs of the minute that opens at <paramref name="minute" />: what was known by its end and nothing after.</summary>
        public IdeaInputs Inputs(IdeaInputs day, DateTimeOffset minute)
        {
            DateTimeOffset end = minute.AddMinutes(1);

            while (_settled < rates.Count && rates[_settled].Time < end)
            {
                _settled++;
            }

            decimal? change = btc.TryGetValue(minute, out decimal now) && btc.TryGetValue(minute - BtcSpan, out decimal before) ? now / before - 1m : null;

            return day with { BtcChange = change, Funding = _settled > 0 ? rates[_settled - 1].Rate : null };
        }
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