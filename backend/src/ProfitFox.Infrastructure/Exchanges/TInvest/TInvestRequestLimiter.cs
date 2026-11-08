using System.Security.Cryptography;
using System.Text;

namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>
///     Lets the requests of a token to a group of methods go at most <c>TInvest:Limits</c> times a minute, a sliding window per token and
///     group, and holds a group after the server said the limit was hit until the reset it named. A request waits before it is sent, so it
///     never waits after it could have gone; a group without a limit in the settings is not counted. The windows are kept by the SHA-256
///     fingerprint of the token, so no token, a wrong one from a form among them, stays in memory.
/// </summary>
internal sealed class TInvestRequestLimiter(TInvestSettings settings, TimeProvider time)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly Lock _gate = new();
    private readonly Dictionary<(string Fingerprint, TInvestMethodGroup Group), Queue<DateTimeOffset>> _sent = [];
    private readonly Dictionary<(string Fingerprint, TInvestMethodGroup Group), DateTimeOffset> _pausedUntil = [];

    /// <summary>Returns once the request may be sent, and counts it as sent then.</summary>
    public async Task AcquireAsync(string token, TInvestMethodGroup group, CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan wait = TryTake(token, group);

            if (wait <= TimeSpan.Zero)
            {
                return;
            }

            await Task.Delay(wait, time, cancellationToken);
        }
    }

    /// <summary>Holds the group of the token until <paramref name="until" />; an earlier end never shortens a later one.</summary>
    public void Pause(string token, TInvestMethodGroup group, DateTimeOffset until)
    {
        (string, TInvestMethodGroup) key = (Fingerprint(token), group);

        lock (_gate)
        {
            if (!_pausedUntil.TryGetValue(key, out DateTimeOffset current) || current < until)
            {
                _pausedUntil[key] = until;
            }
        }
    }

    private static string Fingerprint(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    // Zero when the request was counted and may go; otherwise how long to wait before asking again.
    private TimeSpan TryTake(string token, TInvestMethodGroup group)
    {
        DateTimeOffset now = time.GetUtcNow();
        (string, TInvestMethodGroup) key = (Fingerprint(token), group);

        lock (_gate)
        {
            if (_pausedUntil.TryGetValue(key, out DateTimeOffset pausedUntil) && now < pausedUntil)
            {
                return pausedUntil - now;
            }

            if (!settings.Limits.TryGetValue(group.SettingsKey(), out int limit))
            {
                return TimeSpan.Zero;
            }

            if (!_sent.TryGetValue(key, out Queue<DateTimeOffset>? sent))
            {
                sent = new Queue<DateTimeOffset>();
                _sent[key] = sent;
            }

            while (sent.Count > 0 && sent.Peek() <= now - Window)
            {
                sent.Dequeue();
            }

            if (sent.Count >= limit)
            {
                return sent.Peek() + Window - now;
            }

            sent.Enqueue(now);

            return TimeSpan.Zero;
        }
    }
}