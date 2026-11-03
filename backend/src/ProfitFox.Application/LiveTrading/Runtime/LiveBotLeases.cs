using System.Collections.Concurrent;

using ProfitFox.Application.Workers;
using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.LiveTrading;

namespace ProfitFox.Application.LiveTrading.Runtime;

/// <summary>
///     The leases of the bots a trading Worker keeps and the changes of their owner, in one loop for the Worker: every
///     <see cref="TradingSettings.LeaseRenewal" /> one query renews the leases of all its bots, and every
///     <see cref="WorkerSettings.PollInterval" /> one query reads which of them were written, so neither grows with the bots.
///     A session holds its bot here for the take of its lease and is told the bot as the database holds it, or that it lost it.
/// </summary>
public sealed class LiveBotLeases(ILiveBotRepository repository, TradingSettings trading, WorkerSettings settings, WorkerIdentity worker, TimeProvider time)
{
    // The clocks of the API and of the Worker may differ, and an earlier write may commit later: each poll looks back this far.
    private static readonly TimeSpan LookBack = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<Guid, Holder> _held = new ConcurrentDictionary<Guid, Holder>();

    /// <summary>Holds a bot the Worker has just taken; disposing lets it go.</summary>
    /// <param name="leased">The take of the bot.</param>
    /// <param name="changed">Gets the bot whenever it was written or its lease renewed.</param>
    /// <param name="lost">Called once when the lease of the take is lost.</param>
    public IDisposable Hold(LeasedBot leased, Action<LiveBot> changed, Action lost)
    {
        var holder = new Holder(leased.Epoch, changed, lost, time.GetUtcNow() + trading.LeaseDuration);
        _held[leased.Bot.BotId] = holder;

        return new Release(() => _held.TryRemove(new KeyValuePair<Guid, Holder>(leased.Bot.BotId, holder)));
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
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The Worker stops.
        }
    }

    private async Task RenewAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        KeyValuePair<Guid, Holder>[] held = [.. _held];

        if (held.Length == 0)
        {
            return;
        }

        DateTimeOffset until = now + trading.LeaseDuration;
        IReadOnlyList<LeasedBot> renewed;

        try
        {
            renewed = await repository.RenewAllAsync(worker.Id, [.. held.Select(item => (item.Key, item.Value.Epoch))], until, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Tried again on the next renewal while the lease holds; another Worker may take the bot once it expired.
            foreach (KeyValuePair<Guid, Holder> item in held.Where(item => now + trading.LeaseRenewal >= item.Value.LeasedUntil))
            {
                Lose(item);
            }

            return;
        }

        var back = renewed.ToDictionary(bot => (bot.Bot.BotId, bot.Epoch), bot => bot.Bot);

        foreach (KeyValuePair<Guid, Holder> item in held)
        {
            if (back.TryGetValue((item.Key, item.Value.Epoch), out LiveBot? bot))
            {
                item.Value.LeasedUntil = until;
                item.Value.Changed(bot);
            }
            else
            {
                Lose(item);
            }
        }
    }

    private async Task<DateTimeOffset> PollAsync(DateTimeOffset seen, CancellationToken cancellationToken)
    {
        IReadOnlyList<ChangedLiveBot> changed;

        try
        {
            changed = await repository.GetChangedAsync(worker.Id, seen - LookBack, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The next poll asks again; a renewal hands the bots on meanwhile.
            return seen;
        }

        foreach (ChangedLiveBot bot in changed)
        {
            if (_held.TryGetValue(bot.Bot.BotId, out Holder? holder) && holder.Epoch == bot.Epoch)
            {
                holder.Changed(bot.Bot);
            }
        }

        return changed.Select(bot => bot.UpdatedAt).Append(seen).Max();
    }

    private void Lose(KeyValuePair<Guid, Holder> item)
    {
        if (_held.TryRemove(item))
        {
            item.Value.Lost();
        }
    }

    private sealed class Holder(long epoch, Action<LiveBot> changed, Action lost, DateTimeOffset leasedUntil)
    {
        public long Epoch { get; } = epoch;

        public Action<LiveBot> Changed { get; } = changed;

        public Action Lost { get; } = lost;

        public DateTimeOffset LeasedUntil { get; set; } = leasedUntil;
    }

    private sealed class Release(Action release) : IDisposable
    {
        public void Dispose()
        {
            release();
        }
    }
}