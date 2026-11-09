using System.Threading.Channels;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.Market;
using ProfitFox.Application.Scanner.Backfill;
using ProfitFox.Application.Scanner.Ideas;
using ProfitFox.Application.Scanner.Minute;
using ProfitFox.Application.Scanner.Universe;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Scanner;

/// <summary>
///     The scanner while it holds the stream: the list of pairs, the streams of minute candles and of liquidations, the loads, the minute cycle
///     and the ideas. Nine lanes run side by side: the stream's candles go to storage in batches; the liquidations of linear pairs go to storage
///     in batches, and the breaks of their stream to a table of their own, since the missed events cannot be loaded again; the REST lane catches up the pairs that lag behind, reads the list
///     again, tops up funding and loads history, one piece at a time; the minute cycle computes the snapshot and the live ideas; one lane clears the signals of other versions of the rules; the lane of
///     ideas reads the warm-ups of the live ideas, replays history from storage only in turns side by side, each on a pair of its own, and
///     removes the ideas closed more than a week ago once an hour; one lane of statistics recomputes the buckets and the calibration once an hour, another the buckets of a type × interval whose exit
///     rule changed (requirement "Пересчёт корзин"). A failed step is logged and tried again; only the end of the lease stops the session.
/// </summary>
public sealed class ScannerSession(
    IScannerPairRepository pairs,
    ICandleRepository candles,
    IMarketStreams streams,
    ScannerUniverse universe,
    ScannerBackfill backfill,
    ScannerMinuteCycle minute,
    LiveIdeas ideas,
    IdeasReplay replay,
    IdeaStatistics statistics,
    IdeaRecomputes recomputes,
    ScannerMarket market,
    ILiquidationRepository liquidations,
    IStreamBreakRepository breaks,
    ScannerMetrics metrics,
    IScannerJournal journal,
    TimeProvider time)
{
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FundingTopUp = TimeSpan.FromHours(1);
    private static readonly TimeSpan StatisticsRefresh = TimeSpan.FromHours(1);
    private static readonly TimeSpan ClosedRemoval = TimeSpan.FromHours(1);

    private const string LiquidationStream = "liquidations";

    private readonly Channel<(Instrument, Candle)> _closed = Channel.CreateUnbounded<(Instrument, Candle)>();
    private readonly Channel<(Instrument, LiquidationEvent)> _liquidations = Channel.CreateUnbounded<(Instrument, LiquidationEvent)>();
    private readonly Channel<StreamBreak> _breaks = Channel.CreateUnbounded<StreamBreak>();
    private readonly Lock _lock = new Lock();
    private readonly HashSet<Instrument> _catchUp = [];
    private readonly TaskCompletionSource _cleaned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _work = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    private IAsyncDisposable? _subscription;

    // The start of a break of the liquidation stream not yet written: the last stored event when the session starts, the loss of a connection,
    // or the moment the subscription was taken off to be made again.
    private DateTimeOffset? _liquidationsLostAt;

    /// <summary>Runs until <paramref name="cancellationToken" /> ends the lease; the stream is taken off then.</summary>
    /// <exception cref="ScannerLeaseLostException">Another scanner took the stream.</exception>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await statistics.StartVersionAsync(cancellationToken);
        IReadOnlyList<ScannerPair> stored = await pairs.GetAllAsync(cancellationToken);
        market.Keep(stored);

        try
        {
            market.KeepListed(await universe.RefreshAsync(stored, cancellationToken) ?? []);
        }
        catch (ExchangeUnavailableException exception)
        {
            // During a pause of REST the stream goes on the pairs storage knows; the REST lane reads the list later.
            journal.Failed("pairs", exception);
        }

        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Scenario "Сканер сменил держателя": the stream of the previous holder ended somewhere after its last stored event.
        _liquidationsLostAt = await liquidations.LastTimeAsync(cancellationToken);

        try
        {
            await SubscribeAsync(session.Token);
            Task[] lanes = [WriteCandlesAsync(session.Token), WriteLiquidationsAsync(session.Token), WriteBreaksAsync(session.Token), RestAsync(session.Token), MinutesAsync(session.Token), CleanUpAsync(session.Token), IdeasAsync(session.Token),
                WholeStatisticsAsync(session.Token),
                RuleStatisticsAsync(session.Token)];
            Task first = await Task.WhenAny(lanes);

            // The lanes use the services of the session, so none of them outlives it.
            await session.CancelAsync();
            await Task.WhenAll(lanes.Select(lane => lane.ContinueWith(_ => { }, TaskScheduler.Default)));
            await first;

            // A lane that saw the end of the lease between two steps ends without an exception.
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            if (_subscription is not null)
            {
                await _subscription.DisposeAsync();
            }
        }
    }

    private async Task SubscribeAsync(CancellationToken cancellationToken)
    {
        if (_subscription is not null)
        {
            // Closed once: a subscription that then fails leaves none, and the REST lane makes it again. The liquidations until the next
            // subscription are missed.
            LiquidationsLost(time.GetUtcNow());
            await _subscription.DisposeAsync();
            _subscription = null;
        }

        IAsyncDisposable candleSubscription = await streams.SubscribeMinuteCandlesAsync(
            [.. market.Listed().Select(pair => pair.Instrument)],
            (instrument, candle) => _closed.Writer.TryWrite((instrument, candle)),
            () => CatchUp(market.Listed().Select(pair => pair.Instrument)),
            cancellationToken);
        IAsyncDisposable liquidationSubscription;

        try
        {
            liquidationSubscription = await streams.SubscribeLiquidationsAsync(
                [.. market.Listed().Select(pair => pair.Instrument).Where(instrument => instrument.Category == MarketCategory.Linear)],
                (instrument, liquidation) => _liquidations.Writer.TryWrite((instrument, liquidation)),
                LiquidationsLost,
                LiquidationsRestored,
                cancellationToken);
        }
        catch
        {
            // Both go together, so the REST lane makes both again.
            await candleSubscription.DisposeAsync();

            throw;
        }

        _subscription = new Subscriptions(candleSubscription, liquidationSubscription);
        LiquidationsRestored(time.GetUtcNow());
    }

    /// <summary>The liquidations go to storage in one batch per wake-up; a batch that failed is a break, as nothing can load it again.</summary>
    private async Task WriteLiquidationsAsync(CancellationToken cancellationToken)
    {
        while (await _liquidations.Reader.WaitToReadAsync(cancellationToken))
        {
            var batch = new List<(Instrument Instrument, LiquidationEvent Event)>();

            while (_liquidations.Reader.TryRead(out (Instrument, LiquidationEvent) item))
            {
                batch.Add(item);
            }

            try
            {
                metrics.Liquidated(await liquidations.AddAsync(batch, cancellationToken));
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                journal.Failed(LiquidationStream, exception);
                _breaks.Writer.TryWrite(new StreamBreak(LiquidationStream, batch.Min(item => item.Event.Time), batch.Max(item => item.Event.Time)));
            }
        }
    }

    private async Task WriteBreaksAsync(CancellationToken cancellationToken)
    {
        while (await _breaks.Reader.WaitToReadAsync(cancellationToken))
        {
            while (_breaks.Reader.TryRead(out StreamBreak? streamBreak))
            {
                try
                {
                    await breaks.AddAsync(streamBreak, cancellationToken);
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    journal.Failed("stream-breaks", exception);
                }
            }
        }
    }

    /// <summary>A break starts at the first loss not yet written.</summary>
    private void LiquidationsLost(DateTimeOffset at)
    {
        lock (_lock)
        {
            _liquidationsLostAt ??= at;
        }
    }

    /// <summary>The stream is back: the break since the loss, if any, goes to storage.</summary>
    private void LiquidationsRestored(DateTimeOffset at)
    {
        DateTimeOffset? lostAt;

        lock (_lock)
        {
            lostAt = _liquidationsLostAt;
            _liquidationsLostAt = null;
        }

        if (lostAt is not null)
        {
            _breaks.Writer.TryWrite(new StreamBreak(LiquidationStream, lostAt.Value, at));
        }
    }

    /// <summary>The closed minutes go to storage in one batch per wake-up, then move the coverage.</summary>
    private async Task WriteCandlesAsync(CancellationToken cancellationToken)
    {
        while (await _closed.Reader.WaitToReadAsync(cancellationToken))
        {
            var batch = new List<(Instrument Instrument, Candle Candle)>();

            while (_closed.Reader.TryRead(out (Instrument, Candle) item))
            {
                batch.Add(item);
            }

            try
            {
                await candles.UpsertAsync(Interval.OneMinute, batch, cancellationToken);

                foreach ((Instrument instrument, Candle candle) in batch)
                {
                    market.Closed(instrument, candle);
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                // The minutes stay out of the coverage, so the REST lane loads them again.
                journal.Failed("candles", exception);
            }
        }
    }

    private async Task RestAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset fundingAt = time.GetUtcNow();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                foreach (Instrument instrument in TakeCatchUps())
                {
                    await backfill.CatchUpAsync(instrument, cancellationToken);
                }

                if (_subscription is null)
                {
                    await SubscribeAsync(cancellationToken);
                }

                IReadOnlyList<ScannerPair>? changed = await universe.RefreshAsync(market.All(), cancellationToken);

                if (changed is { Count: > 0 })
                {
                    market.KeepListed(changed);
                    await SubscribeAsync(cancellationToken);
                }

                if (time.GetUtcNow() >= fundingAt)
                {
                    await backfill.TopUpFundingAsync(cancellationToken);
                    fundingAt = time.GetUtcNow() + FundingTopUp;
                }

                if (!await backfill.StepAsync(cancellationToken))
                {
                    // Nothing to load: wait for pairs to catch up or the next check of the list.
                    TaskCompletionSource work = Volatile.Read(ref _work);
                    await Task.WhenAny(work.Task, Task.Delay(TimeSpan.FromMinutes(1), time, cancellationToken));
                    Interlocked.CompareExchange(ref _work, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), work);
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                journal.Failed("rest", exception);
                await Task.Delay(Retry, time, cancellationToken);
            }
        }
    }

    private async Task MinutesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                CatchUp(await minute.RunMinuteAsync(cancellationToken));
            }
            catch (Exception exception) when (exception is not ScannerLeaseLostException && !cancellationToken.IsCancellationRequested)
            {
                journal.Failed("minute", exception);
            }
        }
    }

    // The signals of other versions of the rules go a step at a time in a lane of their own: a step that fails or takes long keeps neither the
    // warm-ups of the live ideas nor the closed ideas waiting, only the history, which is replayed once no signal of another version is left.
    private async Task CleanUpAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!await replay.CleanUpAsync(cancellationToken))
                {
                    _cleaned.TrySetResult();

                    // A lane that ends ends the session, so this one waits for the end of the lease.
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                journal.Failed("ideas-cleanup", exception);
                await Task.Delay(Retry, time, cancellationToken);
            }
        }
    }

    // The cases of this version are replayed after the clean-up, so before it ends there are none to count: on prod the query read the whole table
    // of the other versions for none and outran the timeout every minute.
    private async Task CleanedAsync(CancellationToken cancellationToken)
    {
        await _cleaned.Task.WaitAsync(cancellationToken);
    }

    private async Task IdeasAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset closedAt = time.GetUtcNow();

        try
        {
            await ideas.CatchUpTradeOutcomesAsync(cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The outcomes of the minutes to come are written with the ideas, so the snapshot and the ideas go on without them.
            journal.Failed("trade-outcomes", exception);
        }

        // The turns of the replay that run (decision 2 of speed-up-ideas-replay): they use the services of the session, so none outlives it.
        var turns = new List<Task>();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (time.GetUtcNow() >= closedAt)
                    {
                        await ideas.RemoveClosedAsync(cancellationToken);
                        await CleanUpAutosAsync(cancellationToken);
                        closedAt = time.GetUtcNow() + ClosedRemoval;
                    }

                    // The warm-ups of the live ideas go before the history.
                    bool warmed = await ideas.ReadWarmUpsAsync(cancellationToken);

                    if (warmed)
                    {
                        continue;
                    }

                    if (Ended(turns))
                    {
                        // The pair of a turn that failed is taken again after the retry, the other turns going on meanwhile.
                        await Task.Delay(Retry, time, cancellationToken);

                        continue;
                    }

                    if (!_cleaned.Task.IsCompleted)
                    {
                        // No history to replay before the clean-up ends: the next warm-ups come within a minute, the history once it ends.
                        await Task.WhenAny(Task.Delay(TimeSpan.FromMinutes(1), time, cancellationToken), _cleaned.Task);
                        cancellationToken.ThrowIfCancellationRequested();

                        continue;
                    }

                    while (await replay.StartAsync(cancellationToken) is { } turn)
                    {
                        turns.Add(turn);
                    }

                    // The next warm-ups come within a minute; a turn that ends makes room for the next one at once.
                    await Task.WhenAny([Task.Delay(TimeSpan.FromMinutes(1), time, cancellationToken), .. turns]);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    journal.Failed("ideas", exception);
                    await Task.Delay(Retry, time, cancellationToken);
                }
            }
        }
        finally
        {
            await Task.WhenAll(turns.Select(turn => turn.ContinueWith(_ => { }, TaskScheduler.Default)));
            Ended(turns);
        }
    }

    /// <summary>Takes the turns that ended off the list; a turn that failed is logged. <c>true</c> when one failed.</summary>
    private bool Ended(List<Task> turns)
    {
        var failed = false;

        foreach (Task turn in turns.Where(turn => turn.IsCompleted).ToArray())
        {
            turns.Remove(turn);

            if (turn.Exception?.InnerException is { } exception)
            {
                journal.Failed("ideas", exception);
                failed = true;
            }
        }

        return failed;
    }

    // Next to the closed ideas, once an hour: the old refusals of the autos go and the requests nobody took become refusals. The two do not wait for
    // each other and a failure of either is logged, so neither stops the other, the ideas of the minute or the history.
    private async Task CleanUpAutosAsync(CancellationToken cancellationToken)
    {
        foreach (Func<CancellationToken, Task> step in new Func<CancellationToken, Task>[] { ideas.RemoveOldRefusalsAsync, ideas.ExpireEntriesAsync })
        {
            try
            {
                await step(cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                journal.Failed("autos", exception);
            }
        }
    }

    // While a recompute runs the API and the ideas read the buckets before it: storage replaces them whole when it ends. The whole pass
    // runs over a minute on the market of prod, so the buckets of a rule applied go in a lane of their own; the rules applied while the whole
    // pass ran go there again after it, for it wrote their buckets by the rules before.
    private async Task WholeStatisticsAsync(CancellationToken cancellationToken)
    {
        await CleanedAsync(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                foreach ((IdeaType type, Interval interval) in await statistics.RecomputeAsync(cancellationToken))
                {
                    recomputes.Add(type, interval);
                }

                await Task.Delay(StatisticsRefresh, time, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                journal.Failed("statistics", exception);
                await Task.Delay(Retry, time, cancellationToken);
            }
        }
    }

    private async Task RuleStatisticsAsync(CancellationToken cancellationToken)
    {
        await CleanedAsync(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                IReadOnlyList<(IdeaType Type, Interval Interval)> changed = recomputes.Take();

                for (int index = 0; index < changed.Count; index++)
                {
                    try
                    {
                        await statistics.RecomputeAsync(changed[index].Type, changed[index].Interval, cancellationToken);
                    }
                    catch
                    {
                        // The one that failed and the ones after it go back, to be done after the retry.
                        foreach ((IdeaType type, Interval interval) in changed.Skip(index))
                        {
                            recomputes.Add(type, interval);
                        }

                        throw;
                    }
                }

                await recomputes.WaitAsync(StatisticsRefresh, time, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                journal.Failed("statistics", exception);
                await Task.Delay(Retry, time, cancellationToken);
            }
        }
    }

    private void CatchUp(IEnumerable<Instrument> instruments)
    {
        lock (_lock)
        {
            _catchUp.UnionWith(instruments);
        }

        Volatile.Read(ref _work).TrySetResult();
    }

    private Instrument[] TakeCatchUps()
    {
        lock (_lock)
        {
            Instrument[] taken = [.. _catchUp];
            _catchUp.Clear();

            return taken;
        }
    }

    /// <summary>The candles and the liquidations of the list of pairs, taken off together.</summary>
    private sealed class Subscriptions(IAsyncDisposable candles, IAsyncDisposable liquidations) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await candles.DisposeAsync();
            await liquidations.DisposeAsync();
        }
    }
}