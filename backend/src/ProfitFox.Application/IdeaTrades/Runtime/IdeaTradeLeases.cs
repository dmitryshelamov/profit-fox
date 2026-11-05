using System.Collections.Concurrent;

using ProfitFox.Application.LiveTrading;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.IdeaTrades;

namespace ProfitFox.Application.IdeaTrades.Runtime;

/// <summary>
///     The leases of the trades by ideas a trading Worker keeps and the changes of their owner, in one loop for the Worker: every
///     <see cref="TradingSettings.LeaseRenewal" /> one query renews the leases of all its trades, and every
///     <see cref="WorkerSettings.PollInterval" /> one query reads which of them were written, so neither grows with the trades.
///     A session holds its trade here for the take of its lease and is told the trade as the database holds it, or that it lost it.
/// </summary>
public sealed class IdeaTradeLeases(IIdeaTradeRepository repository, TradingSettings trading, WorkerSettings settings, WorkerIdentity worker, TimeProvider time)
{
    // The clocks of the API and of the Worker may differ, and an earlier write may commit later: each poll looks back this far.
    private static readonly TimeSpan LookBack = TimeSpan.FromSeconds(30);

    // A failed session hands its trade back to any Worker after this pause rather than at the end of its lease: soon enough to
    // watch the trade again, late enough not to ask a failing exchange every second.
    private static readonly TimeSpan HandBackPause = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<Guid, Holder> _held = new ConcurrentDictionary<Guid, Holder>();

    // A renewal and a hand back write the same leases one after the other, so a renewal on the way cannot undo a hand back.
    private readonly SemaphoreSlim _writing = new SemaphoreSlim(1, 1);

    /// <summary>
    ///     Holds a trade the Worker has just taken; disposing lets it go. A take of the trade held before has lost it: its session is
    ///     told so at once rather than at the next renewal.
    /// </summary>
    /// <param name="leased">The take of the trade.</param>
    /// <param name="changed">Gets the trade whenever it was written or its lease renewed.</param>
    /// <param name="lost">Called once when the lease of the take is lost.</param>
    public IDisposable Hold(LeasedIdeaTrade leased, Action<IdeaTrade> changed, Action lost)
    {
        var holder = new Holder(leased.Epoch, changed, lost, time.GetUtcNow() + trading.LeaseDuration);
        Holder? replaced = null;
        _held.AddOrUpdate(leased.Trade.Id, holder, (_, old) =>
        {
            replaced = old;

            return holder;
        });

        if (replaced is not null)
        {
            Tell(replaced.Lost);
        }

        return new Release(() => _held.TryRemove(new KeyValuePair<Guid, Holder>(leased.Trade.Id, holder)));
    }

    /// <summary>
    ///     A session that failed stops holding its trade and shortens the lease of its take to a pause, never past the lease it had.
    ///     A database that does not answer leaves the lease to run out instead.
    /// </summary>
    public async Task HandBackAsync(Guid tradeId, long epoch)
    {
        if (_held.TryGetValue(tradeId, out Holder? holder) && holder.Epoch == epoch)
        {
            _held.TryRemove(new KeyValuePair<Guid, Holder>(tradeId, holder));
        }

        await _writing.WaitAsync();

        try
        {
            TimeSpan pause = HandBackPause < trading.LeaseDuration ? HandBackPause : trading.LeaseDuration;
            await repository.RenewAllAsync(worker.Id, [(tradeId, epoch)], time.GetUtcNow() + pause, CancellationToken.None);
        }
        catch (Exception)
        {
            // The lease runs out as it would have.
        }
        finally
        {
            _writing.Release();
        }
    }

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        DateTimeOffset seen = time.GetUtcNow();
        DateTimeOffset renewal = seen + trading.LeaseRenewal;

        try
        {
            while (true)
            {
                await Task.Delay(settings.PollInterval, time, stoppingToken);
                DateTimeOffset now = time.GetUtcNow();

                if (now >= renewal)
                {
                    await RenewAsync(now, stoppingToken);
                    renewal = now + trading.LeaseRenewal;
                }

                seen = await PollAsync(seen, stoppingToken);
                LoseExpiring();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The Worker stops.
        }
    }

    private async Task RenewAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await _writing.WaitAsync(cancellationToken);

        try
        {
            await RenewHeldAsync(now, cancellationToken);
        }
        finally
        {
            _writing.Release();
        }
    }

    private async Task RenewHeldAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        KeyValuePair<Guid, Holder>[] held = [.. _held];

        if (held.Length == 0)
        {
            return;
        }

        DateTimeOffset until = now + trading.LeaseDuration;
        IReadOnlyList<LeasedIdeaTrade> renewed;

        try
        {
            renewed = await repository.RenewAllAsync(worker.Id, [.. held.Select(item => (item.Key, item.Value.Epoch))], until, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Tried again on the next renewal while the lease holds; the loop gives the trade up before it expires.
            return;
        }

        var back = renewed.ToDictionary(trade => (trade.Trade.Id, trade.Epoch), trade => trade.Trade);

        foreach (KeyValuePair<Guid, Holder> item in held)
        {
            if (back.TryGetValue((item.Key, item.Value.Epoch), out IdeaTrade? trade))
            {
                item.Value.LeasedUntil = until;
                Tell(() => item.Value.Changed(trade));
            }
            else
            {
                Lose(item);
            }
        }
    }

    private async Task<DateTimeOffset> PollAsync(DateTimeOffset seen, CancellationToken cancellationToken)
    {
        IReadOnlyList<ChangedIdeaTrade> changed;

        try
        {
            changed = await repository.GetChangedAsync(worker.Id, seen - LookBack, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The next poll asks again; a renewal hands the trades on meanwhile.
            return seen;
        }

        // The look back reads a row again: it goes on only when it was written anew, later or earlier than the one handed on.
        foreach (ChangedIdeaTrade trade in changed)
        {
            if (_held.TryGetValue(trade.Trade.Id, out Holder? holder) && holder.Epoch == trade.Epoch && holder.HandedOn != trade.UpdatedAt)
            {
                holder.HandedOn = trade.UpdatedAt;
                Tell(() => holder.Changed(trade.Trade));
            }
        }

        return changed.Select(trade => trade.UpdatedAt).Append(seen).Max();
    }

    /// <summary>
    ///     Gives up every trade whose lease would expire before the next renewal, since another Worker may take it then. Counted
    ///     after each round, so calls that fail slowly, as on a database that does not answer, cannot carry a trade past its lease.
    /// </summary>
    private void LoseExpiring()
    {
        DateTimeOffset next = time.GetUtcNow() + trading.LeaseRenewal;

        foreach (KeyValuePair<Guid, Holder> item in _held.Where(item => next >= item.Value.LeasedUntil))
        {
            Lose(item);
        }
    }

    private void Lose(KeyValuePair<Guid, Holder> item)
    {
        if (_held.TryRemove(item))
        {
            Tell(item.Value.Lost);
        }
    }

    /// <summary>A session that fails on what it is told must not stop the loop that keeps the other trades of the Worker.</summary>
    private static void Tell(Action tell)
    {
        try
        {
            tell();
        }
        catch (Exception)
        {
            // The session ends on its own; its lease is lost or renewed with the others.
        }
    }

    private sealed class Holder(long epoch, Action<IdeaTrade> changed, Action lost, DateTimeOffset leasedUntil)
    {
        public long Epoch { get; } = epoch;

        public Action<IdeaTrade> Changed { get; } = changed;

        public Action Lost { get; } = lost;

        public DateTimeOffset LeasedUntil { get; set; } = leasedUntil;

        /// <summary>When the row last handed on to the session was written.</summary>
        public DateTimeOffset? HandedOn { get; set; }
    }

    private sealed class Release(Action release) : IDisposable
    {
        public void Dispose()
        {
            release();
        }
    }
}