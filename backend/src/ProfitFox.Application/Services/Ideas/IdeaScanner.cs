using System.Collections.Concurrent;
using System.Globalization;

using Microsoft.Extensions.Logging;

using ProfitFox.Application.Exceptions;
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

    // How much later than a day before the start the whole catch-up of the feed may begin: the feed subscribes a moment
    // after the start.
    private static readonly TimeSpan CatchUpMargin = TimeSpan.FromMinutes(5);

    // How far the live feed has to be past a moment for a pair with no minute up to it to count as having had no trades
    // then (task 18.10): a minute of another pair may come before the last one of this pair. Not while the feed catches
    // up (task 19.9): a catch-up told in batches by pair is far past the moment before the minutes of the pair come.
    private static readonly TimeSpan PairWait = TimeSpan.FromMinutes(2);

    // How many reasons of failed warm-ups the scanner remembers having told within the minute (task 19.4).
    private const int LoggedReasons = 64;

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

    // The warm-ups read whose candles at the join the book has not closed yet (task 19.3): taken again on every pass.
    private readonly List<Warmup> _joining = [];
    private readonly List<Task> _fetching = [];
    private readonly Dictionary<string, Slot> _slots = new Dictionary<string, Slot>(StringComparer.Ordinal);
    private readonly Dictionary<TimeSpan, DateTimeOffset> _boundaries = [];
    private readonly HashSet<Idea> _dirty = [];
    private readonly List<Idea> _pending = [];

    // The ideas that closed after the last boundary of their timeframe the scanner computed (task 26.1): while the feed
    // catches up, the minutes run ahead of the boundaries, and the rule of one open idea still has to see them there.
    private readonly List<Idea> _ended = [];

    private readonly ILogger? _logger;
    private readonly Dictionary<string, DateTimeOffset> _logged = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
    private string? _lastFailure;
    private Dictionary<string, string> _pairs = new Dictionary<string, string>(StringComparer.Ordinal);
    private List<Idea> _open = [];
    private DateTimeOffset _started;
    private DateTimeOffset? _reconciled;
    private DateTimeOffset? _snapshotMinute;
    private bool _gap;

    // Whether the feed is live: it went quiet with its newest minute recent, so its catch-up is over (task 19.9).
    private bool _live;

    // Whether the feed has been silent for longer than the limit since ideas last counted without a break (task 24.1).
    private bool _silent;

    public IdeaScanner(
        MarketBook book,
        IRecentHistory history,
        IIdeaRepository ideas,
        IMarketPairRepository market,
        IdeaLibrary library,
        TimeProvider time,
        ScannerSettings settings,
        IdeasSettings burst,
        ILogger? logger = null)
    {
        _logger = logger;
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
    ///     the last hole or silence; null while the feed catches up, and while it has told no minute for longer than
    ///     <see cref="ScannerSettings.FeedSilence" /> (task 24.1).
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
    ///     How many pairs of the set the last warm-up read of some enabled strategy or label failed on, and the last reason
    ///     (task 18.3); null while none failed.
    /// </summary>
    public (int Failed, string? Reason) Failures
    {
        get
        {
            Run[] runs = [.. _slots.Values.Select(slot => slot.Current)];
            int failed = _pairs.Keys.Count(symbol => runs.Any(run => run.Pairs.TryGetValue(symbol, out PairState? state) && state.Failure is not null));
            return (failed, failed > 0 ? _lastFailure : null);
        }
    }

    /// <summary>
    ///     The open ideas the scanner walks.
    /// </summary>
    public IReadOnlyList<Idea> Open => _open;

    /// <summary>
    ///     Where the feed is to catch up from: null for the whole day the feed holds, counted when it subscribes (task 18.9),
    ///     or the minute an open idea was counted up to when that is further back, so the feed tells the hole it cannot catch
    ///     up.
    /// </summary>
    public DateTimeOffset? CatchUpFrom
    {
        get
        {
            DateTimeOffset? from = null;
            DateTimeOffset day = _started - CatchUp + CatchUpMargin;
            foreach (Idea idea in _open)
            {
                DateTimeOffset progress = idea.ComputedUntil is { } until ? until : idea.Moment;
                if (progress < day && (from is null || progress < from))
                {
                    from = progress;
                }
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
                        Forget(run, symbol);
                    }
                }

                break;
            case FeedResubscribed:
                // A new subscription after a break (task 26.2) replays the minutes from a little before the last one, by minute
                // and within a minute by pair: a catch-up, until the feed goes quiet again. A break shorter than the limit of
                // silence is no break of the ideas, so since when they count is kept.
                _live = false;
                break;
            case FeedGap gap:
                _gap = true;
                _live = false;
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
        Boundaries(now);
        // The writes go one by one, each on its own (task 18.8): a failed snapshot does not stop the ideas, and the ideas of
        // a candle wait for the database rather than go.
        await Guarded("the new ideas", WritePendingAsync, cancellationToken);
        Walk(now);
        await Guarded("the snapshot of the market", SnapshotAsync, cancellationToken);
        await Guarded("the open ideas", SaveAsync, cancellationToken);
        if (Book.LastMinute is not { } last)
        {
            return;
        }

        if (LiveSince is null && last + OneMinute >= now - TimeSpan.FromMinutes(2))
        {
            LiveSince = _gap ? now : _started;
            if (_silent)
            {
                _silent = false;
                _logger?.LogInformation("The feed told the minutes again up to {Last:O}; ideas count without a break since {Since:O}.", last, now);
            }
        }
        else if (_settings.FeedSilence > TimeSpan.Zero && LiveSince is not null && now - (last + OneMinute) > _settings.FeedSilence)
        {
            // The feed told no minute for longer than the limit (task 24.1): no candle closes, and ideas are not counted
            // without a break. The minutes it catches up when it is back come as a catch-up: the pairs wait for their own
            // minutes until it is live again, and ideas count without a break since then. A limit of zero or less switches
            // the watch off, as it does for the feed (task 25.2).
            LiveSince = null;
            _live = false;
            _gap = true;
            _silent = true;
            _logger?.LogWarning(
                "The feed told no minute after {Last:O} for {Silence}; ideas are not counted until it comes back.",
                last,
                (now - (last + OneMinute)).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    ///     The feed told nothing for a while before this pass (task 19.9): with its newest minute recent, its catch-up is over
    ///     and it is live, so a pair with no minute up to a moment had no trades then once the feed is two minutes past it.
    ///     Until then, and again after a hole, the time and the payments of an idea wait for the minutes of its pair.
    /// </summary>
    public void Quiet()
    {
        if (Book.LastMinute is { } last && last + OneMinute >= _time.GetUtcNow() - PairWait)
        {
            _live = true;
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
    // ideas on the pairs the new one is not warm on yet; a strategy or a label that is off goes. Only a new slot or a new
    // version gets a run with its executor (task 18.13): a pass without a change builds nothing.
    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        (IReadOnlyList<StrategyRunSpec> strategies, IReadOnlyList<LabelRunSpec> labels) = await _library.CurrentAsync(cancellationToken);
        var wanted = new Dictionary<string, (string Key, Func<Run> Create)>(StringComparer.Ordinal);
        foreach (StrategyRunSpec spec in strategies)
        {
            wanted[$"s:{spec.Id}:{spec.Timeframe.Ticks}"] = (spec.Key, () => new Run(spec.Key, spec.Timeframe, StrategyGraph.NamedPairs(spec.Strategy.Graph))
            {
                Strategy = spec,
                Executor = new StrategyExecutor(spec.Strategy)
            });
        }

        foreach (LabelRunSpec spec in labels)
        {
            wanted[$"l:{spec.SignalId}"] = (spec.Key, () => new Run(spec.Key, spec.Timeframe, StrategyGraph.NamedPairs(spec.Signal.Graph))
            {
                Label = spec,
                Signals = new SignalExecutor(spec.Signal)
            });
        }

        foreach (string slot in _slots.Keys.Where(slot => !wanted.ContainsKey(slot)).ToList())
        {
            _slots.Remove(slot);
        }

        foreach ((string key, (string runKey, Func<Run> create)) in wanted)
        {
            if (!_slots.TryGetValue(key, out Slot? slot))
            {
                _slots[key] = new Slot(create());
            }
            else if (slot.Current.Key != runKey)
            {
                _slots[key] = new Slot(create()) { Previous = slot.Current.Pairs.Values.Any(pair => pair.State == WarmState.Warm) ? slot.Current : slot.Previous };
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
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The service did not answer, refused or timed out — the timeout of the REST is a cancellation too, but not by the
            // stop of the scanner (task 19.2): the pair is read again later, and the reason is told.
            _warmed.Enqueue(new Warmup(run, symbol, Loading: true, [], [], null)
            {
                Failure = exception.Message,
                Reason = exception is HistoryUnavailableException refusal ? refusal.Reason : exception.GetType().Name
            });
        }
        finally
        {
            _parallel.Release();
        }
    }

    // The graphs of the warmed pairs go through the history read; what they ordered there is no idea.
    private void ApplyWarmups(DateTimeOffset now)
    {
        List<Warmup> ready = [.. _joining];
        _joining.Clear();
        while (_warmed.TryDequeue(out Warmup? read))
        {
            ready.Add(read);
        }

        foreach (Warmup taken in ready)
        {
            Warmup warmup = taken;
            if (!warmup.Run.Pairs.TryGetValue(warmup.Symbol, out PairState? state) || state.State != WarmState.Reading)
            {
                continue;
            }

            if (!warmup.Loading)
            {
                Join join = JoinOf(warmup, out DateTimeOffset at);
                if (join == Join.Waiting)
                {
                    // The book still catches up the candles after those of the REST: the pair waits for it, without a new read.
                    _joining.Add(warmup);
                    continue;
                }

                if (join == Join.Missed)
                {
                    // The book closed the boundary without the candle at the join — the REST lags behind the start of the book,
                    // or a hole came after the read (task 20.4): it will not come, so the pair is read from the REST again.
                    warmup = warmup with
                    {
                        Loading = true,
                        Failure = $"The book does not hold the candle of {at.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} at the join of the warm-up after the REST",
                        Reason = "The book does not hold the candle at the join of the warm-up"
                    };
                }
            }

            state.Failure = warmup.Failure;
            if (warmup.Failure is { } failure)
            {
                Failed(warmup.Symbol, warmup.Reason ?? failure, failure, now);
            }

            if (warmup.Loading)
            {
                state.State = WarmState.Waiting;
                state.RetryAt = now + _settings.WarmupRetry;
                continue;
            }

            // A warm-up starts the pair from nothing: a pair that left the catalog and came back has no state of before.
            Forget(warmup.Run, warmup.Symbol);
            state = new PairState { State = WarmState.Reading };
            warmup.Run.Pairs[warmup.Symbol] = state;
            try
            {
                foreach ((ClosedCandle candle, FundingPayment? funding, IReadOnlyDictionary<string, ClosedCandle>? pairs) in PairCandles.WithPairs(
                             warmup.Run.Timeframe,
                             warmup.Candles,
                             warmup.Funding,
                             warmup.Named))
                {
                    Step(warmup.Run, warmup.Symbol, candle, funding, pairs);
                    state.WarmedUntil = candle.CloseTime;
                }

                FillFromBook(warmup.Run, warmup.Symbol, state);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                PairFailed(warmup.Run, warmup.Symbol, exception, now);
                continue;
            }

            state.State = WarmState.Warm;
        }
    }

    // A failed warm-up goes to the journal no more than once a minute for one reason — the code and what was asked, not the
    // body of the answer (task 19.4): 831 pairs refused alike give one line whatever their bodies. The reasons told are kept
    // for a minute and no more than a bounded number of them.
    private void Failed(string symbol, string reason, string failure, DateTimeOffset now)
    {
        _lastFailure = failure;
        if (_logged.TryGetValue(reason, out DateTimeOffset logged) && now - logged < OneMinute)
        {
            return;
        }

        if (_logged.Count >= LoggedReasons)
        {
            foreach (string told in _logged.Where(item => now - item.Value >= OneMinute).Select(item => item.Key).ToList())
            {
                _logged.Remove(told);
            }

            if (_logged.Count >= LoggedReasons)
            {
                _logged.Clear();
            }
        }

        _logged[reason] = now;
        _logger?.LogWarning("The warm-up of {Symbol} failed and is read again in {Retry}: {Failure}", symbol, _settings.WarmupRetry, failure);
    }

    // Whether the book holds the candles between the last one of the REST and the boundary the pair is filled up to: only a
    // candle the book closed and has every minute of is taken from it (task 19.3). The pair waits while the book has not
    // closed the boundary yet, or while the catch-up has not told the minutes of the pair up to it (task 20.5); once the book
    // closed it, a candle at the join it does not hold is missed for good (task 20.4).
    // <paramref name="at" /> is the open of the candle at the join.
    private Join JoinOf(Warmup warmup, out DateTimeOffset at)
    {
        at = DateTimeOffset.MinValue;
        if (warmup.Candles.Count == 0)
        {
            return Join.Joined;
        }

        TimeSpan timeframe = warmup.Run.Timeframe;
        DateTimeOffset until = FilledUntil(timeframe);
        at = warmup.Candles[^1].OpenTime + timeframe;
        if (at >= until)
        {
            return Join.Joined;
        }

        if (!Book.Closed(until))
        {
            return Join.Waiting;
        }

        if (!Book.Holds(timeframe, at))
        {
            return Join.Missed;
        }

        // The book closed the boundary for every pair, but a catch-up told one pair at a time may not have told this one yet
        // (task 20.5): until the feed is live the pair waits for its own minute of the boundary; once it is live, a pair
        // without that minute had no trades then, and a quiet pair does not wait for ever.
        return _live || (Book.LastMinuteOf(warmup.Symbol) is { } last && last + OneMinute >= until) ? Join.Joined : Join.Waiting;
    }

    private DateTimeOffset FilledUntil(TimeSpan timeframe)
    {
        return _boundaries.TryGetValue(timeframe, out DateTimeOffset seen) ? seen : Floor(_started, timeframe);
    }

    // The candles between the last one the REST gave and the last boundary the scanner computed come from the book (task
    // 18.15): the REST may not have the candle of that boundary yet, and the graph would see a hole. They are of the warm-up:
    // what the graph orders on them is no idea.
    private void FillFromBook(Run run, string symbol, PairState state)
    {
        DateTimeOffset until = FilledUntil(run.Timeframe);
        DateTimeOffset from = state.WarmedUntil == DateTimeOffset.MinValue ? until : state.WarmedUntil;
        for (DateTimeOffset close = from + run.Timeframe; close <= until; close += run.Timeframe)
        {
            if (Book.Candle(symbol, run.Timeframe, close - run.Timeframe) is not { } candle)
            {
                continue;
            }

            Dictionary<string, ClosedCandle>? pairs = null;
            foreach (string named in run.Named)
            {
                if (Book.Candle(named, run.Timeframe, close - run.Timeframe) is { } other)
                {
                    pairs ??= new Dictionary<string, ClosedCandle>(StringComparer.Ordinal);
                    pairs[named] = other;
                }
            }

            Step(run, symbol, candle, Book.LastPayment(symbol, close), pairs ?? (run.Named.Count > 0 ? new Dictionary<string, ClosedCandle>() : null));
            state.WarmedUntil = close;
        }
    }

    // The graph of a pair failed: the pair goes back to waiting, with a line in the journal, and is warmed up again later;
    // the other pairs go on (task 18.8).
    private void PairFailed(Run run, string symbol, Exception exception, DateTimeOffset now)
    {
        _logger?.LogWarning(exception, "The graph of {Run} on {Symbol} failed; the pair is warmed up again in {Retry}.", run.Key, symbol, _settings.WarmupRetry);
        Forget(run, symbol);
        run.Pairs[symbol] = new PairState { State = WarmState.Waiting, RetryAt = now + _settings.WarmupRetry };
    }

    private static void Forget(Run run, string symbol)
    {
        run.Pairs.Remove(symbol);
        run.Values.Remove(symbol);
        run.Executor?.Forget(symbol);
        run.Signals?.Forget(symbol);
    }

    private async Task Guarded(string what, Func<CancellationToken, Task> write, CancellationToken cancellationToken)
    {
        try
        {
            await write(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger?.LogError(exception, "The scanner could not write {What}; it tries again on the next pass.", what);
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
    // and the ideas of a strategy on a candle get the labels and the burst of that close. The ideas wait in the pending ones
    // until they are written: a failed write keeps them for the next pass (task 18.8).
    private void Boundaries(DateTimeOffset now)
    {
        // The former run of a label may be on another timeframe while the new one warms up (task 18.14).
        TimeSpan[] timeframes = [.. _slots.Values.SelectMany(slot => slot.Previous is { } previous ? [slot.Current.Timeframe, previous.Timeframe] : new[] { slot.Current.Timeframe }).Distinct()];
        SortedDictionary<DateTimeOffset, List<TimeSpan>> closes = [];
        foreach (TimeSpan timeframe in timeframes)
        {
            DateTimeOffset last = _boundaries.TryGetValue(timeframe, out DateTimeOffset seen) ? seen : Floor(_started, timeframe);
            for (DateTimeOffset close = last + timeframe; BoundaryClosed(close); close += timeframe)
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

        // A payment at a close belongs to the next candle (task 27.3), as in a backtest: the live close of a candle waits
        // for no payment, and every pair is computed at once.
        foreach ((DateTimeOffset close, List<TimeSpan> at) in closes)
        {
            WalkPending(close);
            List<Idea> born = Compute(close, at, [.. _pairs.Keys], now);
            Burst(born, born);
            _pending.AddRange(born);
        }
    }

    // The labels, then the strategies, on the candles of the pairs that close at <paramref name="close" />. Gives the ideas.
    private List<Idea> Compute(DateTimeOffset close, List<TimeSpan> at, IReadOnlyCollection<string> symbols, DateTimeOffset now)
    {
        foreach (Slot slot in _slots.Values.Where(slot => slot.Current.Label is not null && Closes(slot, at)))
        {
            StepAll(slot, close, at, now, symbols);
        }

        List<Idea> born = [];
        foreach (Slot slot in _slots.Values.Where(slot => slot.Current.Strategy is not null && Closes(slot, at)))
        {
            born.AddRange(StepAll(slot, close, at, now, symbols));
        }

        return born;
    }

    // «⚡ всплеск» on the ideas of <paramref name="labelled" /> whose side and timeframe have at least the threshold of pairs
    // among the ideas of the candle, <paramref name="candle" />.
    private void Burst(IReadOnlyCollection<Idea> candle, IReadOnlyCollection<Idea> labelled)
    {
        foreach (IGrouping<(TimeSpan, EntrySide), Idea> group in candle.GroupBy(idea => (idea.Timeframe, idea.Side)))
        {
            if (group.Select(idea => idea.Symbol).Distinct().Count() >= _burst.BurstOf(group.Key.Item1))
            {
                foreach (Idea idea in group.Where(idea => labelled.Contains(idea) && !idea.Labels.Contains(IdeasSettings.BurstLabel)))
                {
                    idea.Labels = [.. idea.Labels, IdeasSettings.BurstLabel];
                }
            }
        }
    }

    // An idea born at an earlier close of this pass waits for its write and was not walked yet (task 26.1): before the next
    // close it goes through the minutes of its pair before that close, so a stop or a target it reached ends it for the rule
    // of one open idea there, as the trade of a backtest ends. A pass of the live feed computes one close and has none such.
    private void WalkPending(DateTimeOffset close)
    {
        foreach (Idea idea in _pending.Where(idea => idea.Moment < close && idea.State == IdeaState.Open && idea.ExitTime is null))
        {
            DateTimeOffset from = idea.ComputedUntil is { } until ? until + OneMinute : idea.Moment;
            foreach (LiveMinute minute in Book.Minutes(idea.Symbol, from, close < idea.Until ? close : idea.Until))
            {
                if (IdeaCycle.Minute(idea, Candle(minute), _costs))
                {
                    break;
                }
            }
        }
    }

    // Whether the candles that close at the moment are closed for every pair. The live feed tells the minute before the close
    // of every pair within the delay of the book. A catch-up is long past the delay already at the first pair of that minute
    // (task 25.1): the service tells it by minute and within a minute by instrument, and a pass in the middle of the batch
    // would compute the other pairs without their last minute for good. Until the feed is live (at the start, after a hole
    // and after a silence) the boundary waits for a minute that opens at the close or later; a catch-up that ends with the
    // minute before the close is computed once the feed is live.
    private bool BoundaryClosed(DateTimeOffset close)
    {
        return Book.Closed(close) && (_live || Book.Past(close));
    }

    // The new ideas in the order they were born; one written goes to the open ones, and a failed write leaves the rest.
    private async Task WritePendingAsync(CancellationToken cancellationToken)
    {
        while (_pending.Count > 0)
        {
            Idea idea = _pending[0];
            if (await _ideas.AddAsync(idea, cancellationToken))
            {
                _open.Add(idea);
            }

            _pending.RemoveAt(0);
        }
    }

    // The runs of a slot on the candle of every pair that closes at <paramref name="close" />: the current run where it is
    // warm, the former version where only it is, on the pairs of <paramref name="symbols" />. Gives the ideas the strategy
    // ordered.
    private List<Idea> StepAll(Slot slot, DateTimeOffset close, List<TimeSpan> at, DateTimeOffset now, IReadOnlyCollection<string> symbols)
    {
        List<Idea> born = [];
        foreach (string symbol in symbols)
        {
            Run? run = RunAt(slot, symbol, close, at);
            TimeSpan timeframe = run?.Timeframe ?? slot.Current.Timeframe;
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

            StrategyStep? step;
            try
            {
                step = Step(run, symbol, candle, Book.LastPayment(symbol, close), pairs ?? (run.Named.Count > 0 ? new Dictionary<string, ClosedCandle>() : null));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                PairFailed(run, symbol, exception, now);
                continue;
            }

            run.Pairs[symbol].WarmedUntil = close;
            // An idea of the strategy on the pair and timeframe stops the order, unless its exit is by this close: an open one,
            // one still waiting for its write, and one the walk already closed past this close while the feed caught up (task
            // 26.1) — the trade of a backtest holds the position up to its exit however far the minutes ran.
            if (step is null || run.Strategy is not { } strategy || step.Entry is null
                || _open.Concat(_ended).Concat(_pending).Any(idea => idea.State is (IdeaState.Open or IdeaState.Closed) && idea.StrategyId == strategy.Id && idea.Symbol == symbol
                                                                    && idea.Timeframe == timeframe && (idea.ExitTime ?? idea.Until) > close))
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

    // The run of a slot that computes the pair at the close: the current one where it is ready, else the former one; each on
    // the closes of its own timeframe.
    private static Run? RunAt(Slot slot, string symbol, DateTimeOffset close, List<TimeSpan> at)
    {
        return (at.Contains(slot.Current.Timeframe) ? Ready(slot.Current, symbol, close) : null)
               ?? (slot.Previous is { } previous && at.Contains(previous.Timeframe) ? Ready(previous, symbol, close) : null);
    }

    private static bool Closes(Slot slot, List<TimeSpan> at)
    {
        return at.Contains(slot.Current.Timeframe) || (slot.Previous is { } previous && at.Contains(previous.Timeframe));
    }

    private static bool Warm(Run run, string symbol)
    {
        return run.Pairs.TryGetValue(symbol, out PairState? state) && state.State == WarmState.Warm;
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
            // The former label while the new one is not warm on the pair (task 18.14).
            Run run = !Warm(slot.Current, symbol) && slot.Previous is { } previous ? previous : slot.Current;
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
                if (IdeaCycle.Minute(idea, Candle(minute), _costs))
                {
                    break;
                }
            }

            // The time and a payment wait for the minutes of the pair to reach their moment (task 18.10).
            if (idea.ExitTime is null && Book.Closed(idea.Until) && Reached(idea.Symbol, idea.Until, last))
            {
                IdeaCycle.TimeUp(idea);
            }

            DateTimeOffset? funded = idea.FundingUntil;
            foreach (FundingPayment payment in Book.Payments(idea.Symbol).Where(payment => payment.Time <= last + OneMinute && Reached(idea.Symbol, payment.Time, last)))
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

    // Whether the minutes of the pair reached the moment: its minute before the moment came, or the live feed is far enough
    // past the moment that the pair had no trades then (task 19.9: not while the feed catches up).
    private bool Reached(string symbol, DateTimeOffset moment, DateTimeOffset last)
    {
        return (Book.LastMinuteOf(symbol) is { } pair && pair + OneMinute >= moment) || (_live && last >= moment + PairWait);
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
        // One statement a pass for every idea that changed (task 18.6).
        await _ideas.SaveAllAsync([.. _dirty], cancellationToken);
        _dirty.Clear();
        // A closed idea whose exit the boundaries of its timeframe have not reached stays for the rule of one open idea (task
        // 26.1); the live feed closes an idea after the boundaries before its exit, so there it stays for no boundary.
        _ended.AddRange(_open.Where(idea => idea.State == IdeaState.Closed && idea.ExitTime > FilledUntil(idea.Timeframe)));
        _ended.RemoveAll(idea => idea.ExitTime <= FilledUntil(idea.Timeframe) || (Book.LastMinute is { } last && idea.ExitTime < last - MarketBook.Kept));
        _open.RemoveAll(idea => idea.State != IdeaState.Open);
    }

    private static ClosedCandle Candle(LiveMinute minute)
    {
        return new ClosedCandle(minute.OpenTime, minute.OpenTime + OneMinute, minute.Open, minute.High, minute.Low, minute.Close, minute.Volume);
    }

    private static DateTimeOffset Floor(DateTimeOffset moment, TimeSpan step)
    {
        return new DateTimeOffset(moment.UtcTicks - (moment.UtcTicks % step.Ticks), TimeSpan.Zero);
    }

    private enum Join
    {
        Joined,
        Waiting,
        Missed
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

        // Why the last warm-up read failed; null when it did not.
        public string? Failure { get; set; }
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
        IReadOnlyDictionary<string, IReadOnlyList<HistoryCandle>>? Named)
    {
        public string? Failure { get; init; }

        // The failure without what changes from one read to the next: the key of the journal (task 19.4).
        public string? Reason { get; init; }
    }
}
