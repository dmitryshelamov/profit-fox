using System.Collections.Concurrent;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Ports.MarketData.Feed;
using ProfitFox.Application.Services.Market;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Execution;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.Services.Signals;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Application.Services.Ideas;

/// <summary>
///     The scanner of ideas (change add-ideas-and-market, design.md, decisions 3–8): the market book fed by the live feeds,
///     the enabled strategies and labels run on the closed candles of every warmed pair, the open ideas walked minute by
///     minute to their outcome, and the snapshot of the market written after every closed minute. One reader drives it:
///     <see cref="ApplyAsync" /> for each event of the feed, <see cref="TickAsync" /> after them and when the feed is quiet.
///     Only the reading of warm-up history runs in the background. Candles that closed before the scanner started give no
///     ideas, and neither do the orders of the candles of a warm-up.
/// </summary>
public sealed class IdeaScanner
{
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CatchUp = TimeSpan.FromHours(24);

    private readonly IRecentHistory _history;
    private readonly IIdeaRepository _ideas;
    private readonly IMarketPairRepository _market;
    private readonly IdeaLibrary _library;
    private readonly TimeProvider _time;
    private readonly ScannerSettings _settings;
    private readonly IdeasSettings _burst;
    private readonly TradeCosts _costs = TradeCosts.Default;
    private readonly SemaphoreSlim _parallel;
    private readonly ConcurrentQueue<Warmup> _warmed = new ConcurrentQueue<Warmup>();
    private readonly List<Task> _fetching = [];
    private readonly Dictionary<string, Slot> _slots = new Dictionary<string, Slot>(StringComparer.Ordinal);
    private readonly Dictionary<TimeSpan, DateTimeOffset> _boundaries = [];
    private readonly HashSet<Idea> _dirty = [];
    private Dictionary<string, string> _pairs = new Dictionary<string, string>(StringComparer.Ordinal);
    private List<Idea> _open = [];
    private DateTimeOffset _started;
    private DateTimeOffset? _reconciled;
    private DateTimeOffset? _snapshotMinute;
    private bool _gap;

    public IdeaScanner(
        MarketBook book,
        IRecentHistory history,
        IIdeaRepository ideas,
        IMarketPairRepository market,
        IdeaLibrary library,
        TimeProvider time,
        ScannerSettings settings,
        IdeasSettings burst)
    {
        Book = book;
        _history = history;
        _ideas = ideas;
        _market = market;
        _library = library;
        _time = time;
        _settings = settings;
        _burst = burst;
        _parallel = new SemaphoreSlim(Math.Max(1, settings.WarmupParallelism));
    }

    public MarketBook Book { get; }

    /// <summary>
    ///     Since when ideas are counted without a break: the start of the scanner once the feed caught up, or the end of
    ///     the last hole; null while the feed catches up.
    /// </summary>
    public DateTimeOffset? LiveSince { get; private set; }

    /// <summary>
    ///     How many pairs of the set every enabled strategy and label is warmed up on, and how many pairs there are.
    /// </summary>
    public (int Warmed, int Pairs) Progress
    {
        get
        {
            Run[] runs = [.. _slots.Values.Select(slot => slot.Current)];
            int warmed = _pairs.Keys.Count(symbol => runs.All(run => run.Pairs.TryGetValue(symbol, out PairState? state) && state.State == WarmState.Warm));
            return (warmed, _pairs.Count);
        }
    }

    /// <summary>
    ///     The open ideas the scanner walks.
    /// </summary>
    public IReadOnlyList<Idea> Open => _open;

    /// <summary>
    ///     Where the feed is to catch up from: a day back, or further when an open idea was counted only up to before that,
    ///     so the feed tells the hole it cannot catch up.
    /// </summary>
    public DateTimeOffset CatchUpFrom
    {
        get
        {
            DateTimeOffset from = _started - CatchUp + OneMinute;
            foreach (Idea idea in _open)
            {
                DateTimeOffset progress = idea.ComputedUntil is { } until ? until : idea.Moment;
                from = progress < from ? progress : from;
            }

            return from;
        }
    }

    /// <summary>
    ///     Starts the scanner now: the open ideas are read to be walked on.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _started = _time.GetUtcNow();
        _open = [.. await _ideas.OpenAsync(cancellationToken)];
    }

    public Task ApplyAsync(MarketEvent item, CancellationToken cancellationToken)
    {
        Book.Apply(item);
        switch (item)
        {
            case MarketPairs listed:
                _pairs = listed.Pairs.ToDictionary(pair => pair.Symbol, pair => pair.Id, StringComparer.Ordinal);
                foreach (Idea idea in _open.Where(idea => idea.State == IdeaState.Open && !_pairs.ContainsKey(idea.Symbol)))
                {
                    IdeaCycle.Exclude(idea, IdeaCycle.Delisted);
                    _dirty.Add(idea);
                }

                foreach (Run run in Runs())
                {
                    foreach (string symbol in run.Pairs.Keys.Where(symbol => !_pairs.ContainsKey(symbol)).ToList())
                    {
                        run.Pairs.Remove(symbol);
                    }
                }

                break;
            case FeedGap gap:
                _gap = true;
                LiveSince = null;
                foreach (Idea idea in _open.Where(idea => idea.State == IdeaState.Open))
                {
                    DateTimeOffset progress = idea.ComputedUntil is { } until ? until + OneMinute : idea.Moment;
                    if (progress < gap.Until && gap.From < idea.Until)
                    {
                        IdeaCycle.Exclude(idea, IdeaCycle.NoData);
                        _dirty.Add(idea);
                    }
                }

                break;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Does what the clock and the book allow: looks at the enabled strategies again when it is time, warms the new
    ///     pairs up, computes the candles that closed, walks the open ideas and writes the snapshot of the market.
    /// </summary>
    public async Task TickAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = _time.GetUtcNow();
        if (_reconciled is null || now - _reconciled >= _settings.ReconcileInterval)
        {
            await ReconcileAsync(cancellationToken);
            _reconciled = now;
        }

        WarmUp(now, cancellationToken);
        ApplyWarmups(now);
        Walk(now);
        await BoundariesAsync(cancellationToken);
        Walk(now);
        await SnapshotAsync(cancellationToken);
        await SaveAsync(cancellationToken);
        if (LiveSince is null && Book.LastMinute is { } last && last + OneMinute >= now - TimeSpan.FromMinutes(2))
        {
            LiveSince = _gap ? now : _started;
        }
    }

    /// <summary>
    ///     Waits for the warm-up reads under way; a test drives the scanner with it.
    /// </summary>
    public async Task WarmupsReadAsync()
    {
        Task[] fetching;
        lock (_fetching)
        {
            fetching = [.. _fetching];
        }

        await Task.WhenAll(fetching);
    }

    private IEnumerable<Run> Runs()
    {
        foreach (Slot slot in _slots.Values)
        {
            yield return slot.Current;
            if (slot.Previous is { } previous)
            {
                yield return previous;
            }
        }
    }

    // The enabled strategies and labels as they are now: a new version is warmed up while the former one goes on giving
    // ideas on the pairs the new one is not warm on yet; a strategy or a label that is off goes.
    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        var wanted = new Dictionary<string, Run>(StringComparer.Ordinal);
        foreach (StrategyRunSpec spec in await _library.StrategiesAsync(cancellationToken))
        {
            wanted[$"s:{spec.Id}:{spec.Timeframe.Ticks}"] = new Run(spec.Key, spec.Timeframe, StrategyGraph.NamedPairs(spec.Strategy.Graph)) { Strategy = spec, Executor = new StrategyExecutor(spec.Strategy) };
        }

        foreach (LabelRunSpec spec in await _library.LabelsAsync(cancellationToken))
        {
            wanted[$"l:{spec.SignalId}"] = new Run(spec.Key, spec.Timeframe, StrategyGraph.NamedPairs(spec.Signal.Graph)) { Label = spec, Signals = new SignalExecutor(spec.Signal) };
        }

        foreach (string slot in _slots.Keys.Where(slot => !wanted.ContainsKey(slot)).ToList())
        {
            _slots.Remove(slot);
        }

        foreach ((string key, Run run) in wanted)
        {
            if (!_slots.TryGetValue(key, out Slot? slot))
            {
                _slots[key] = new Slot(run);
            }
            else if (slot.Current.Key != run.Key)
            {
                _slots[key] = new Slot(run) { Previous = slot.Current.Pairs.Values.Any(pair => pair.State == WarmState.Warm) ? slot.Current : slot.Previous };
            }
            else if (slot.Previous is not null && _pairs.Keys.All(symbol => slot.Current.Pairs.TryGetValue(symbol, out PairState? state) && state.State == WarmState.Warm))
            {
                // The new version is warm on every pair: the former one is done.
                _slots[key] = new Slot(slot.Current);
            }
        }
    }

    // Starts the reading of the history of every pair a run is not warm on yet.
    private void WarmUp(DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (Run run in _slots.Values.Select(slot => slot.Current))
        {
            foreach ((string symbol, string instrument) in _pairs)
            {
                if (run.Pairs.TryGetValue(symbol, out PairState? state) && (state.State != WarmState.Waiting || state.RetryAt > now))
                {
                    continue;
                }

                state ??= new PairState();
                run.Pairs[symbol] = state;
                state.State = WarmState.Reading;
                Task reading = ReadWarmupAsync(run, symbol, instrument, now, cancellationToken);
                lock (_fetching)
                {
                    _fetching.Add(reading);
                }
            }
        }

        lock (_fetching)
        {
            _fetching.RemoveAll(task => task.IsCompleted);
        }
    }

    private async Task ReadWarmupAsync(Run run, string symbol, string instrument, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await _parallel.WaitAsync(cancellationToken);
        try
        {
            RecentRead<HistoryCandle> candles = await _history.Candles(instrument, run.Timeframe, now, _settings.WarmupCandles, cancellationToken);
            if (candles.Loading)
            {
                _warmed.Enqueue(new Warmup(run, symbol, Loading: true, [], [], null));
                return;
            }

            DateTimeOffset from = candles.Items.Count > 0 ? candles.Items[0].OpenTime - TimeSpan.FromDays(1) : now - TimeSpan.FromDays(2);
            RecentRead<HistoryPayment> funding = await _history.Funding(instrument, from, now, cancellationToken);
            Dictionary<string, IReadOnlyList<HistoryCandle>>? named = null;
            foreach (string pair in run.Named)
            {
                if (_pairs.TryGetValue(pair, out string? id) && await _history.Candles(id, run.Timeframe, now, _settings.WarmupCandles, cancellationToken) is { Loading: false } read)
                {
                    named ??= new Dictionary<string, IReadOnlyList<HistoryCandle>>(StringComparer.Ordinal);
                    named[pair] = read.Items;
                }
            }

            _warmed.Enqueue(new Warmup(run, symbol, funding.Loading, candles.Items, funding.Items, named));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The service did not answer: the pair is read again later.
            _warmed.Enqueue(new Warmup(run, symbol, Loading: true, [], [], null));
        }
        finally
        {
            _parallel.Release();
        }
    }

    // The graphs of the warmed pairs go through the history read; what they ordered there is no idea.
    private void ApplyWarmups(DateTimeOffset now)
    {
        while (_warmed.TryDequeue(out Warmup? warmup))
        {
            if (!warmup.Run.Pairs.TryGetValue(warmup.Symbol, out PairState? state))
            {
                continue;
            }

            if (warmup.Loading)
            {
                state.State = WarmState.Waiting;
                state.RetryAt = now + _settings.WarmupRetry;
                continue;
            }

            foreach ((ClosedCandle candle, FundingPayment? funding, IReadOnlyDictionary<string, ClosedCandle>? pairs) in PairCandles.WithPairs(
                         warmup.Run.Timeframe,
                         warmup.Candles,
                         warmup.Funding,
                         warmup.Named))
            {
                Step(warmup.Run, warmup.Symbol, candle, funding, pairs);
                state.WarmedUntil = candle.CloseTime;
            }

            state.State = WarmState.Warm;
        }
    }

    // A strategy is stepped for its order; a label keeps the value of its signal at the close.
    private static StrategyStep? Step(Run run, string symbol, ClosedCandle candle, FundingPayment? funding, IReadOnlyDictionary<string, ClosedCandle>? pairs)
    {
        if (run.Executor is { } executor)
        {
            return executor.Step(symbol, candle, funding, pairs);
        }

        run.Values[symbol] = (candle.CloseTime, run.Signals!.Value(symbol, candle, funding, pairs));
        return null;
    }

    // Every candle closed since the last pass, in the order of the closes; at one close the labels go before the strategies,
    // and the ideas of a strategy on a candle get the labels and the burst of that close.
    private async Task BoundariesAsync(CancellationToken cancellationToken)
    {
        TimeSpan[] timeframes = [.. _slots.Values.Select(slot => slot.Current.Timeframe).Distinct()];
        SortedDictionary<DateTimeOffset, List<TimeSpan>> closes = [];
        foreach (TimeSpan timeframe in timeframes)
        {
            DateTimeOffset last = _boundaries.TryGetValue(timeframe, out DateTimeOffset seen) ? seen : Floor(_started, timeframe);
            for (DateTimeOffset close = last + timeframe; Book.Closed(close); close += timeframe)
            {
                if (!closes.TryGetValue(close, out List<TimeSpan>? at))
                {
                    at = [];
                    closes[close] = at;
                }

                at.Add(timeframe);
                last = close;
            }

            _boundaries[timeframe] = last;
        }

        foreach ((DateTimeOffset close, List<TimeSpan> at) in closes)
        {
            foreach (Slot slot in _slots.Values.Where(slot => slot.Current.Label is not null && at.Contains(slot.Current.Timeframe)))
            {
                StepAll(slot, close);
            }

            List<Idea> born = [];
            foreach (Slot slot in _slots.Values.Where(slot => slot.Current.Strategy is not null && at.Contains(slot.Current.Timeframe)))
            {
                born.AddRange(StepAll(slot, close));
            }

            foreach (IGrouping<(TimeSpan, EntrySide), Idea> group in born.GroupBy(idea => (idea.Timeframe, idea.Side)))
            {
                if (group.Select(idea => idea.Symbol).Distinct().Count() >= _burst.BurstOf(group.Key.Item1))
                {
                    foreach (Idea idea in group)
                    {
                        idea.Labels = [.. idea.Labels, IdeasSettings.BurstLabel];
                    }
                }
            }

            foreach (Idea idea in born)
            {
                if (await _ideas.AddAsync(idea, cancellationToken))
                {
                    _open.Add(idea);
                }
            }
        }
    }

    // The runs of a slot on the candle of every pair that closes at <paramref name="close" />: the current run where it is
    // warm, the former version where only it is. Gives the ideas the strategy ordered.
    private List<Idea> StepAll(Slot slot, DateTimeOffset close)
    {
        List<Idea> born = [];
        TimeSpan timeframe = slot.Current.Timeframe;
        foreach (string symbol in _pairs.Keys)
        {
            Run? run = Ready(slot.Current, symbol, close) ?? (slot.Previous is { } previous ? Ready(previous, symbol, close) : null);
            if (run is null || Book.Candle(symbol, timeframe, close - timeframe) is not { } candle)
            {
                continue;
            }

            Dictionary<string, ClosedCandle>? pairs = null;
            foreach (string named in run.Named)
            {
                if (Book.Candle(named, timeframe, close - timeframe) is { } other)
                {
                    pairs ??= new Dictionary<string, ClosedCandle>(StringComparer.Ordinal);
                    pairs[named] = other;
                }
            }

            StrategyStep? step = Step(run, symbol, candle, Book.LastPayment(symbol, close), pairs ?? (run.Named.Count > 0 ? new Dictionary<string, ClosedCandle>() : null));
            run.Pairs[symbol].WarmedUntil = close;
            // An open idea of the strategy on the pair and timeframe stops the order, unless its exit is by this close.
            if (step is null || run.Strategy is not { } strategy || step.Entry is null
                || _open.Any(idea => idea.State == IdeaState.Open && idea.StrategyId == strategy.Id && idea.Symbol == symbol && idea.Timeframe == timeframe
                                     && (idea.ExitTime ?? idea.Until) > close))
            {
                continue;
            }

            if (IdeaBirth.Of(step, candle, strategy.Id, strategy.Name, strategy.Version, timeframe, symbol, LabelsOf(symbol, close)) is { } idea)
            {
                born.Add(idea);
            }
        }

        return born;
    }

    private static Run? Ready(Run run, string symbol, DateTimeOffset close)
    {
        return run.Pairs.TryGetValue(symbol, out PairState? state) && state.State == WarmState.Warm && close > state.WarmedUntil ? run : null;
    }

    // The texts of the labels whose signal is true on the pair at the last candle of their timeframe closed by the moment.
    private List<string> LabelsOf(string symbol, DateTimeOffset moment)
    {
        List<string> texts = [];
        foreach (Slot slot in _slots.Values.Where(slot => slot.Current.Label is not null).OrderBy(slot => slot.Current.Label!.Text, StringComparer.Ordinal))
        {
            Run run = slot.Current;
            if (run.Values.TryGetValue(symbol, out (DateTimeOffset Close, bool? Value) value) && value.Close == Floor(moment, run.Timeframe) && value.Value == true)
            {
                texts.Add(run.Label!.Text);
            }
        }

        return texts;
    }

    // The open ideas walked through the minutes and payments of the book: the time up once every minute before it came, the
    // outcome once the payment at the exit could come, and the result now with each new minute.
    private void Walk(DateTimeOffset now)
    {
        if (Book.LastMinute is not { } last)
        {
            return;
        }

        foreach (Idea idea in _open.Where(idea => idea.State == IdeaState.Open))
        {
            DateTimeOffset? before = idea.ComputedUntil;
            DateTimeOffset from = idea.ComputedUntil is { } until ? until + OneMinute : idea.Moment;
            foreach (LiveMinute minute in Book.Minutes(idea.Symbol, from, idea.Until))
            {
                var candle = new ClosedCandle(minute.OpenTime, minute.OpenTime + OneMinute, minute.Open, minute.High, minute.Low, minute.Close, minute.Volume);
                if (IdeaCycle.Minute(idea, candle, _costs))
                {
                    break;
                }
            }

            if (idea.ExitTime is null && Book.Closed(idea.Until))
            {
                IdeaCycle.TimeUp(idea);
            }

            DateTimeOffset? funded = idea.FundingUntil;
            foreach (FundingPayment payment in Book.Payments(idea.Symbol).Where(payment => payment.Time <= last + OneMinute))
            {
                if (Book.CloseBefore(idea.Symbol, payment.Time) is { } price)
                {
                    IdeaCycle.Payment(idea, payment.Time, (double)payment.Rate, IdeaCycle.Price(price));
                }
            }

            if (idea.ExitTime is { } exit && idea.State == IdeaState.Open && now >= exit + _settings.ExitWait && last + OneMinute >= exit)
            {
                IdeaCycle.Close(idea, _costs);
            }

            DateTimeOffset at = last + OneMinute;
            if (idea.State == IdeaState.Open && idea.ExitTime is null && idea.NowAt != at && Book.CloseBefore(idea.Symbol, at) is { } close)
            {
                idea.Now = IdeaCycle.Now(idea, IdeaCycle.Price(close), at, _costs);
                idea.NowAt = at;
                _dirty.Add(idea);
            }

            if (idea.ComputedUntil != before || idea.FundingUntil != funded || idea.State != IdeaState.Open || idea.ExitTime is not null)
            {
                _dirty.Add(idea);
            }
        }
    }

    private async Task SnapshotAsync(CancellationToken cancellationToken)
    {
        if (Book.LastMinute is not { } last || last == _snapshotMinute || _pairs.Count == 0)
        {
            return;
        }

        MarketPair[] pairs = [.. _pairs.Keys.Select(symbol => Book.Snapshot(symbol)).OfType<MarketPair>()];
        await _market.ReplaceAsync(pairs, cancellationToken);
        _snapshotMinute = last;
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        foreach (Idea idea in _dirty)
        {
            await _ideas.SaveAsync(idea, cancellationToken);
        }

        _dirty.Clear();
        _open.RemoveAll(idea => idea.State != IdeaState.Open);
    }

    private static DateTimeOffset Floor(DateTimeOffset moment, TimeSpan step)
    {
        return new DateTimeOffset(moment.UtcTicks - (moment.UtcTicks % step.Ticks), TimeSpan.Zero);
    }

    private enum WarmState
    {
        Waiting,
        Reading,
        Warm
    }

    private sealed class PairState
    {
        public WarmState State { get; set; } = WarmState.Waiting;

        public DateTimeOffset WarmedUntil { get; set; } = DateTimeOffset.MinValue;

        public DateTimeOffset RetryAt { get; set; } = DateTimeOffset.MinValue;
    }

    // A strategy on one timeframe or a label, with its executor and the state of each pair.
    private sealed class Run(string key, TimeSpan timeframe, IReadOnlyList<string> named)
    {
        public string Key { get; } = key;

        public TimeSpan Timeframe { get; } = timeframe;

        public IReadOnlyList<string> Named { get; } = named;

        public StrategyRunSpec? Strategy { get; init; }

        public LabelRunSpec? Label { get; init; }

        public StrategyExecutor? Executor { get; init; }

        public SignalExecutor? Signals { get; init; }

        public Dictionary<string, PairState> Pairs { get; } = new Dictionary<string, PairState>(StringComparer.Ordinal);

        public Dictionary<string, (DateTimeOffset Close, bool? Value)> Values { get; } = new Dictionary<string, (DateTimeOffset Close, bool? Value)>(StringComparer.Ordinal);
    }

    // The run of a strategy on a timeframe or of a label, and its former version while the current one warms up.
    private sealed class Slot(Run current)
    {
        public Run Current { get; } = current;

        public Run? Previous { get; init; }
    }

    private sealed record Warmup(
        Run Run,
        string Symbol,
        bool Loading,
        IReadOnlyList<HistoryCandle> Candles,
        IReadOnlyList<HistoryPayment> Funding,
        IReadOnlyDictionary<string, IReadOnlyList<HistoryCandle>>? Named);
}
