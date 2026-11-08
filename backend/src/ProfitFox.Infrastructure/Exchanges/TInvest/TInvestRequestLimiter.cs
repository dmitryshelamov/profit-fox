namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>
///     Lets the requests of a token to a group of methods go at most <c>TInvest:Limits</c> times a minute, a sliding window per token and
///     group, and holds a group after the server said the limit was hit until the reset it named. A request waits before it is sent, so it
///     never waits after it could have gone; a group without a limit in the settings is not counted.
/// </summary>
internal sealed class TInvestRequestLimiter(TInvestSettings settings, TimeProvider time)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly Lock _gate = new();
    private readonly Dictionary<(string Token, TInvestMethodGroup Group), Queue<DateTimeOffset>> _sent = [];
    private readonly Dictionary<(string Token, TInvestMethodGroup Group), DateTimeOffset> _pausedUntil = [];

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
        lock (_gate)
        {
            if (!_pausedUntil.TryGetValue((token, group), out DateTimeOffset current) || current < until)
            {
                _pausedUntil[(token, group)] = until;
            }
        }
    }

    // Zero when the request was counted and may go; otherwise how long to wait before asking again.
    private TimeSpan TryTake(string token, TInvestMethodGroup group)
    {
        DateTimeOffset now = time.GetUtcNow();

        lock (_gate)
        {
            if (_pausedUntil.TryGetValue((token, group), out DateTimeOffset pausedUntil) && now < pausedUntil)
            {
                return pausedUntil - now;
            }

            if (!settings.Limits.TryGetValue(group.SettingsKey(), out int limit))
            {
                return TimeSpan.Zero;
            }

            if (!_sent.TryGetValue((token, group), out Queue<DateTimeOffset>? sent))
            {
                sent = new Queue<DateTimeOffset>();
                _sent[(token, group)] = sent;
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