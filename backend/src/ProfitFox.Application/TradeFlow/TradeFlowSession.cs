using System.Collections.Concurrent;

using ProfitFox.Application.Ports.Market;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.TradeFlow;

/// <summary>
///     The collector while it holds the trade flow (decisions 3, 6 and 9 of collect-trade-flow). It subscribes the trades of the listed pairs of
///     linear and spot from the list of the scanner, reads the list again every <see cref="PairsRefresh" />, subscribes a new pair on its own
///     and stops writing a pair out of trading. Every <see cref="WriteInterval" /> the lane of writes takes the closed minutes into a queue and
///     writes the queue in one batch, the breaks of the stream (<c>trades</c>) next to it; a batch that failed stays in the queue and is tried
///     again, until storage refuses it for the lease, which ends the session. A lane of its own rebuilds the closed days from the daily archive.
/// </summary>
public sealed class TradeFlowSession(
    IScannerPairRepository pairs,
    ITradeFlowMinuteRepository minutes,
    IStreamBreakRepository breaks,
    IMarketStreams streams,
    TradeFlowRebuild rebuild,
    TradeFlowMetrics metrics,
    WorkerIdentity identity,
    ITradeFlowJournal journal,
    TimeProvider time)
{
    /// <summary>How often the collector reads the list of pairs again.</summary>
    public static readonly TimeSpan PairsRefresh = TimeSpan.FromMinutes(5);

    /// <summary>How often the lane of writes takes the closed minutes and writes what waits.</summary>
    public static readonly TimeSpan WriteInterval = TimeSpan.FromSeconds(5);

    /// <summary>How often the collector builds the queue of days to rebuild from the archive again.</summary>
    public static readonly TimeSpan ArchiveRefresh = TimeSpan.FromHours(1);

    private const string Stream = "trades";

    private readonly TradeFlowMinutes _minutes = new TradeFlowMinutes(time);
    private readonly Dictionary<(Instrument Instrument, DateTimeOffset OpenTime), TradeFlowMinute> _unsaved = [];
    private readonly ConcurrentQueue<StreamBreak> _breaks = new ConcurrentQueue<StreamBreak>();
    private readonly List<IAsyncDisposable> _subscriptions = [];
    private readonly HashSet<Instrument> _subscribed = [];
    private readonly Lock _lock = new Lock();
    private volatile HashSet<Instrument> _listed = [];

    // The start of a break not yet written: the end of the last stored minute when the session starts, or the loss of a connection.
    private DateTimeOffset? _lostAt;

    /// <summary>Runs until <paramref name="cancellationToken" /> ends the lease; the stream is taken off then.</summary>
    /// <exception cref="TradeFlowLeaseLostException">Another collector took the trade flow.</exception>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _listed = await ListedAsync(cancellationToken);

        // Scenario "Перезапуск при выкатке": the stream of the previous holder ended after its last stored minute.
        _lostAt = await minutes.LastAsync(cancellationToken) is { } last ? last.AddMinutes(1) : null;

        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            await SubscribeAsync([.. _listed], cancellationToken);
            Restored(time.GetUtcNow());
            Task[] lanes = [WriteAsync(session.Token), PairsAsync(session.Token), ArchiveAsync(session.Token)];
            Task first = await Task.WhenAny(lanes);

            await session.CancelAsync();
            await Task.WhenAll(lanes.Select(lane => lane.ContinueWith(_ => { }, TaskScheduler.Default)));
            await first;
        }
        finally
        {
            foreach (IAsyncDisposable subscription in _subscriptions)
            {
                await subscription.DisposeAsync();
            }
        }
    }

    private async Task<HashSet<Instrument>> ListedAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ScannerPair> stored = await pairs.GetAllAsync(cancellationToken);

        return [.. stored.Where(pair => pair.Listed && pair.Instrument.Category != MarketCategory.Moex).Select(pair => pair.Instrument)];
    }

    private async Task SubscribeAsync(IReadOnlyList<Instrument> instruments, CancellationToken cancellationToken)
    {
        IAsyncDisposable subscription = await streams.SubscribeTradesAsync(instruments, Traded, Lost, Restored, cancellationToken);
        _subscriptions.Add(subscription);
        _subscribed.UnionWith(instruments);
    }

    /// <summary>A pair out of trading stays subscribed until the next session, but its trades are no longer counted.</summary>
    private void Traded(Instrument instrument, TradeEvent trade)
    {
        if (_listed.Contains(instrument))
        {
            _minutes.Add(instrument, trade);
            metrics.Traded(instrument.Category);
        }
    }

    /// <summary>A break starts at the first loss not yet written.</summary>
    private void Lost(DateTimeOffset at)
    {
        lock (_lock)
        {
            _lostAt ??= at;
        }
    }

    /// <summary>The stream is back: the break since the loss, if any, goes to storage with the next write.</summary>
    private void Restored(DateTimeOffset at)
    {
        lock (_lock)
        {
            if (_lostAt is { } lostAt)
            {
                _breaks.Enqueue(new StreamBreak(Stream, lostAt, at));
            }

            _lostAt = null;
        }
    }

    private async Task PairsAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(PairsRefresh, time, cancellationToken);

            try
            {
                HashSet<Instrument> listed = await ListedAsync(cancellationToken);
                _listed = listed;
                Instrument[] added = [.. listed.Except(_subscribed)];

                if (added.Length > 0)
                {
                    await SubscribeAsync(added, cancellationToken);
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                journal.Failed("pairs", exception);
            }
        }
    }

    /// <summary>
    ///     The rebuild of closed days from the archive, right after the start and then every <see cref="ArchiveRefresh" />, apart from the
    ///     writes of the minutes; a pass that failed is logged and the next one starts over.
    /// </summary>
    private async Task ArchiveAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                await rebuild.RunAsync([.. _listed], cancellationToken);
            }
            catch (Exception exception) when (exception is not TradeFlowLeaseLostException && !cancellationToken.IsCancellationRequested)
            {
                journal.Failed("archive", exception);
            }

            await Task.Delay(ArchiveRefresh, time, cancellationToken);
        }
    }

    private async Task WriteAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(WriteInterval, time, cancellationToken);
            await WriteBreaksAsync(cancellationToken);

            foreach ((Instrument instrument, TradeFlowMinute minute) in _minutes.TakeClosed())
            {
                _unsaved[(instrument, minute.OpenTime)] = minute;
            }

            if (_unsaved.Count == 0)
            {
                continue;
            }

            bool saved;

            try
            {
                saved = await minutes.SaveAsync(identity.Id, [.. _unsaved.Select(item => (item.Key.Instrument, item.Value))], cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                journal.Failed("minutes", exception);

                continue;
            }

            if (!saved)
            {
                throw new TradeFlowLeaseLostException();
            }

            _unsaved.Clear();
        }
    }

    private async Task WriteBreaksAsync(CancellationToken cancellationToken)
    {
        while (_breaks.TryDequeue(out StreamBreak? streamBreak))
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