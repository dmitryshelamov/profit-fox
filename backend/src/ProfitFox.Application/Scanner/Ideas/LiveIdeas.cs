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
///     reads the setups of the last minute of the complete pairs and stores the ideas made and moved.
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

    private readonly IdeaEngine _engine = new IdeaEngine(settings.Live(), IdeasEngineVersion.Current);
    private readonly ConcurrentQueue<Warming> _warming = new ConcurrentQueue<Warming>();
    private readonly ConcurrentDictionary<Instrument, bool> _read = new ConcurrentDictionary<Instrument, bool>();
    private readonly Dictionary<Instrument, IdeaCandle> _fed = [];
    private Dictionary<Instrument, List<Idea>>? _open;
    private Dictionary<Instrument, Dictionary<Interval, decimal>> _percentiles = [];
    private DateTimeOffset? _percentilesAt;

    /// <summary>Reads the warm-up of the next pairs not warmed up yet; <c>false</c> when there is none to read.</summary>
    public async Task<bool> ReadWarmUpsAsync(CancellationToken cancellationToken)
    {
        ScannerPair[] pending = [.. market.Listed().Where(pair => pair.CoveredUntil is not null && market.HoldsMinutes(pair.Instrument) && !_read.ContainsKey(pair.Instrument))];

        foreach (ScannerPair pair in pending)
        {
            var closed = new Dictionary<Interval, IReadOnlyList<IdeaCandle>>();

            foreach (Interval interval in IdeasSettings.Intervals)
            {
                DateTimeOffset to = interval.CandleOpen(pair.CoveredUntil!.Value);
                TimeSpan length = interval.CandleEnd(to) - to;
                closed[interval] = await candles.GetAggregatedAsync(pair.Instrument, interval, to - (length * IdeaSeries.Capacity), to, cancellationToken);
            }

            _read[pair.Instrument] = true;
            _warming.Enqueue(new Warming(pair.Instrument, closed));
        }

        return pending.Length > 0;
    }

    /// <summary>The ideas of the minute that ends at <paramref name="end" />; returns the setups of the complete pairs.</summary>
    public async Task<IReadOnlyDictionary<Instrument, IReadOnlyList<PairSetup>>> RunAsync(DateTimeOffset end, CancellationToken cancellationToken)
    {
        _open ??= (await ideas.GetOpenAsync(cancellationToken)).GroupBy(idea => idea.Instrument).ToDictionary(group => group.Key, group => group.ToList());
        await RefreshPercentilesAsync(cancellationToken);

        while (_warming.TryDequeue(out Warming? warming))
        {
            Warm(warming);
        }

        var setups = new Dictionary<Instrument, IReadOnlyList<PairSetup>>();
        var created = new List<Idea>();
        var changed = new List<Idea>();

        foreach (ScannerPair pair in market.Listed().Where(pair => _fed.ContainsKey(pair.Instrument)))
        {
            DateTimeOffset until = pair.CoveredUntil!.Value < end ? pair.CoveredUntil.Value : end;
            bool complete = !pair.IsBehind(end);
            IdeaCandle last = _fed[pair.Instrument];
            MarketTicker? ticker = market.TickerOf(pair.Instrument);
            var inputs = new IdeaInputs(ticker is { Bid: { } bid, Ask: { } ask } ? ask - bid : null, _percentiles.GetValueOrDefault(pair.Instrument) ?? []);

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
        await ideas.AddAsync(created, cancellationToken);
        await ideas.UpdateAsync(changed, cancellationToken);

        return setups;
    }

    private void Warm(Warming warming)
    {
        ScannerPair pair = market.Find(warming.Instrument)!;
        IReadOnlyList<IdeaCandle> known = Known(warming.Instrument);
        IReadOnlyList<IdeaCandle> minutes = IdeaCandles.Minutes(known, known[0].OpenTime, pair.CoveredUntil!.Value, known[0].Open);
        _engine.Warm(warming.Instrument, warming.Closed, minutes, _open!.GetValueOrDefault(warming.Instrument) ?? []);
        _fed[warming.Instrument] = minutes[^1];
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
            .ToDictionary(group => group.Key, group => group.ToDictionary(item => item.Interval, item => item.Percentile));
        _percentilesAt = now;
    }

    private sealed record Warming(Instrument Instrument, IReadOnlyDictionary<Interval, IReadOnlyList<IdeaCandle>> Closed);
}