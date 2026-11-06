using System.Collections.Concurrent;

using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Scanner.Ideas;

/// <summary>
///     The live ideas of the scanner (requirement "Сетапы и идеи в минутном цикле"). The lane of ideas reads the closed candles a pair warms
///     up with from storage; the minute cycle, after the snapshot, warms up the pairs read, feeds each pair its minutes up to its coverage,
///     reads the setups of the last minute of the complete pairs, closes the ideas of pairs out of trading and stores the ideas made and moved
///     in one transaction. Storage stays the truth: a failed write starts every pair over from it, an idea it refused starts its pair over.
/// </summary>
public sealed class LiveIdeas(
    IIdeaRepository ideas,
    IIdeaVolatilityRepository volatility,
    ICandleRepository candles,
    ScannerMarket market,
    IdeasSettings settings,
    TimeProvider time)
{
    private static readonly TimeSpan PercentilesRefresh = TimeSpan.FromHours(1);
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);
    private static readonly TimeSpan ClosedKept = TimeSpan.FromDays(7);

    private readonly IdeaEngine _engine = new IdeaEngine(settings.Rules(), IdeasEngineVersion.Current);
    private readonly ConcurrentQueue<Warming> _warming = new ConcurrentQueue<Warming>();
    // The generation a pair was read in; a start over begins a new one, and the warm-ups of an older generation are dropped.
    private readonly ConcurrentDictionary<Instrument, int> _read = new ConcurrentDictionary<Instrument, int>();
    private int _generation;
    private readonly Dictionary<Instrument, IdeaCandle> _fed = [];
    private Dictionary<Instrument, List<Idea>>? _open;
    private Dictionary<Instrument, Dictionary<Interval, VolatilityPercentiles>> _percentiles = [];
    private DateTimeOffset? _percentilesAt;

    /// <summary>Reads the warm-up of the next pairs not warmed up yet; <c>false</c> when there is none to read.</summary>
    public async Task<bool> ReadWarmUpsAsync(CancellationToken cancellationToken)
    {
        int generation = Volatile.Read(ref _generation);
        ScannerPair[] pending =
        [
            .. market.Listed().Where(pair => pair.CoveredUntil is not null
                                             && !(_read.TryGetValue(pair.Instrument, out int read) && read == generation)
                                             && market.HoldsMinutes(pair.Instrument)
                                             && market.Minutes(pair.Instrument).Count > 0)
        ];

        foreach (ScannerPair pair in pending)
        {
            var closed = new Dictionary<Interval, IReadOnlyList<IdeaCandle>>();

            foreach (Interval interval in IdeasSettings.Intervals)
            {
                DateTimeOffset to = interval.CandleOpen(pair.CoveredUntil!.Value);
                TimeSpan length = interval.CandleEnd(to) - to;
                closed[interval] = await candles.GetAggregatedAsync(pair.Instrument, interval, to - (length * IdeaSeries.Capacity), to, cancellationToken);
            }

            // Memory holds a day of minutes; a longer window of a setup takes the minutes before from storage.
            IReadOnlyList<IdeaCandle> stored = [];

            if (_engine.Window > Day.TotalMinutes)
            {
                DateTimeOffset until = pair.CoveredUntil!.Value;
                stored = [.. (await candles.GetAsync(pair.Instrument, Interval.OneMinute, until.AddMinutes(-_engine.Window), until, _engine.Window, cancellationToken))
                    .Select(candle => new IdeaCandle(candle.OpenTime, candle.Open.Value, candle.High.Value, candle.Low.Value, candle.Close.Value, candle.Turnover.Value))];
            }

            _read[pair.Instrument] = generation;
            _warming.Enqueue(new Warming(pair.Instrument, closed, stored, generation));
        }

        return pending.Length > 0;
    }

    /// <summary>Deletes from storage the ideas closed more than a week ago (requirement "Срок хранения закрытых идей").</summary>
    public Task RemoveClosedAsync(CancellationToken cancellationToken)
    {
        return ideas.DeleteClosedBeforeAsync(time.GetUtcNow() - ClosedKept, cancellationToken);
    }

    /// <summary>The ideas of the minute that ends at <paramref name="end" />; returns the setups of the complete pairs.</summary>
    public async Task<IReadOnlyDictionary<Instrument, IReadOnlyList<PairSetup>>> RunAsync(DateTimeOffset end, CancellationToken cancellationToken)
    {
        _open ??= (await ideas.GetOpenAsync(cancellationToken)).GroupBy(idea => idea.Instrument).ToDictionary(group => group.Key, group => group.ToList());
        await RefreshPercentilesAsync(cancellationToken);

        var setups = new Dictionary<Instrument, IReadOnlyList<PairSetup>>();
        var created = new List<Idea>();
        var changed = new List<Idea>();

        while (_warming.TryDequeue(out Warming? warming))
        {
            if (warming.Generation == _generation)
            {
                changed.AddRange(Warm(warming));
            }
        }

        changed.AddRange(ExpireOutOfTrading(end));

        foreach (ScannerPair pair in market.Listed().Where(pair => _fed.ContainsKey(pair.Instrument)))
        {
            DateTimeOffset until = pair.CoveredUntil!.Value < end ? pair.CoveredUntil.Value : end;
            bool complete = !pair.IsBehind(end);
            IdeaCandle last = _fed[pair.Instrument];
            IdeaInputs inputs = Inputs(pair.Instrument);

            foreach (IdeaCandle minute in IdeaCandles.Minutes(Known(pair.Instrument), last.OpenTime.AddMinutes(1), until, last.Close))
            {
                bool detect = complete && minute.OpenTime.AddMinutes(1) == end;
                IdeaStep step = _engine.Step(pair.Instrument, minute, inputs, detect);
                created.AddRange(step.Created);
                changed.AddRange(step.Changed);
                _fed[pair.Instrument] = minute;

                if (detect)
                {
                    setups[pair.Instrument] = step.Setups;
                }
            }
        }

        // Ideas made this minute enter only on the next one, so none of them moved yet.
        IReadOnlyList<Idea> added;

        try
        {
            added = await ideas.SaveAsync(created, changed, cancellationToken);
        }
        catch
        {
            StartOver();
            throw;
        }

        foreach (Instrument instrument in created.Except(added).Select(idea => idea.Instrument).Distinct())
        {
            Forget(instrument);
        }

        return setups;
    }

    /// <summary>Warms a pair up with the minutes in memory and its open ideas in storage.</summary>
    private IReadOnlyList<Idea> Warm(Warming warming)
    {
        IReadOnlyList<IdeaCandle> memory = Known(warming.Instrument);
        IdeaCandle[] known = [.. warming.Stored.Where(minute => minute.OpenTime < memory[0].OpenTime), .. memory];
        ScannerPair pair = market.Find(warming.Instrument)!;
        IReadOnlyList<IdeaCandle> minutes = IdeaCandles.Minutes(known, known[0].OpenTime, pair.CoveredUntil!.Value, known[0].Open);
        IReadOnlyList<Idea> moved = _engine.Warm(warming.Instrument, warming.Closed, minutes, _open!.GetValueOrDefault(warming.Instrument) ?? [], Inputs(pair.Instrument));
        _fed[warming.Instrument] = minutes[^1];

        return moved;
    }

    /// <summary>
    ///     The open ideas of the pairs out of trading expire at the end of the minute, at the last close known. The ones only in storage first
    ///     move through the minutes in memory, as a warm-up would move them.
    /// </summary>
    private List<Idea> ExpireOutOfTrading(DateTimeOffset end)
    {
        var expired = new List<Idea>();

        foreach (Instrument instrument in _open!.Keys.Union(_fed.Keys).Where(instrument => market.Find(instrument) is not { Listed: true }).ToList())
        {
            bool warm = _fed.Remove(instrument, out IdeaCandle? last);
            IReadOnlyList<Idea> open = warm ? _engine.Open(instrument) : _open.GetValueOrDefault(instrument) ?? [];

            if (!warm && market.Find(instrument) is not null && Known(instrument) is { Count: > 0 } known)
            {
                IReadOnlyList<IdeaCandle> minutes = IdeaCandles.Minutes(known, known[0].OpenTime, known[^1].OpenTime.AddMinutes(1), known[0].Open);
                open = IdeaEngine.CatchUp(open, minutes);
                last = minutes[^1];
            }

            expired.AddRange(open.Select(idea => idea.IsOpen ? idea with { Track = IdeaTracker.Expire(idea.Track, end, last?.Close) } : idea));
            _open.Remove(instrument);
            _read.TryRemove(instrument, out _);
        }

        return expired;
    }

    private IdeaInputs Inputs(Instrument instrument)
    {
        MarketTicker? ticker = market.TickerOf(instrument);

        return new IdeaInputs(
            ticker is { Bid: { } bid, Ask: { } ask } ? ask - bid : null,
            _percentiles.GetValueOrDefault(instrument) ?? [],
            market.Find(instrument)?.TickSize);
    }

    /// <summary>Drops the state of every pair: storage is read again and the pairs warm up again from it.</summary>
    private void StartOver()
    {
        Interlocked.Increment(ref _generation);
        _fed.Clear();
        _open = null;
    }

    /// <summary>Drops the state of one pair, which warms up again from storage.</summary>
    private void Forget(Instrument instrument)
    {
        _fed.Remove(instrument);
        _read.TryRemove(instrument, out _);
        _open = null;
    }

    private IReadOnlyList<IdeaCandle> Known(Instrument instrument)
    {
        return [.. market.Minutes(instrument).Select(minute => new IdeaCandle(minute.OpenTime, minute.Open, minute.High, minute.Low, minute.Close, minute.Turnover))];
    }

    private async Task RefreshPercentilesAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();

        if (_percentilesAt is { } at && now - at < PercentilesRefresh)
        {
            return;
        }

        _percentiles = (await volatility.GetAllAsync(cancellationToken))
            .GroupBy(item => item.Instrument)
            .ToDictionary(group => group.Key, group => group.ToDictionary(item => item.Interval, item => new VolatilityPercentiles(item.Low, item.Percentile)));
        _percentilesAt = now;
    }

    private sealed record Warming(Instrument Instrument, IReadOnlyDictionary<Interval, IReadOnlyList<IdeaCandle>> Closed, IReadOnlyList<IdeaCandle> Stored, int Generation);
}