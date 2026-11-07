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
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Scanner;

/// <summary>
///     The scanner while it holds the stream: the list of pairs, the stream of minute candles, the loads, the minute cycle and the ideas. Seven
///     lanes run side by side: the stream's candles go to storage in batches; the REST lane catches up the pairs that lag behind, reads the list
///     again, tops up funding and loads history, one piece at a time; the minute cycle computes the snapshot and the live ideas; one lane clears the signals of other versions of the rules; the lane of
///     ideas reads the warm-ups of the live ideas, replays history from storage only and removes the ideas closed more than a week ago once an
///     hour; one lane of statistics recomputes the buckets and the calibration once an hour, another the buckets of a type × interval whose exit
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
    IScannerJournal journal,
    TimeProvider time)
{
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FundingTopUp = TimeSpan.FromHours(1);
    private static readonly TimeSpan StatisticsRefresh = TimeSpan.FromHours(1);
    private static readonly TimeSpan ClosedRemoval = TimeSpan.FromHours(1);

    private readonly Channel<(Instrument, Candle)> _closed = Channel.CreateUnbounded<(Instrument, Candle)>();
    private readonly Lock _lock = new Lock();
    private readonly HashSet<Instrument> _catchUp = [];
    private readonly TaskCompletionSource _cleaned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _work = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    private IAsyncDisposable? _subscription;

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

        try
        {
            await SubscribeAsync(session.Token);
            Task[] lanes = [WriteCandlesAsync(session.Token), RestAsync(session.Token), MinutesAsync(session.Token), CleanUpAsync(session.Token), IdeasAsync(session.Token),
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
            // Closed once: a subscription that then fails leaves none, and the REST lane makes it again.
            await _subscription.DisposeAsync();
            _subscription = null;
        }

        _subscription = await streams.SubscribeMinuteCandlesAsync(
            [.. market.Listed().Select(pair => pair.Instrument)],
            (instrument, candle) => _closed.Writer.TryWrite((instrument, candle)),
            () => CatchUp(market.Listed().Select(pair => pair.Instrument)),
            cancellationToken);
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

                if (!_cleaned.Task.IsCompleted)
                {
                    // No history to replay before the clean-up ends: the next warm-ups come within a minute, the history once it ends.
                    await Task.WhenAny(Task.Delay(TimeSpan.FromMinutes(1), time, cancellationToken), _cleaned.Task);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                else if (!await replay.StepAsync(cancellationToken))
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), time, cancellationToken);
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                journal.Failed("ideas", exception);
                await Task.Delay(Retry, time, cancellationToken);
            }
        }
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
}