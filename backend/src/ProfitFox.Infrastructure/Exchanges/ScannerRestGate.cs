using Microsoft.Extensions.Options;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Scanner;
using ProfitFox.Domain.Repositories;
using ProfitFox.Infrastructure.Exchanges.Retry;

namespace ProfitFox.Infrastructure.Exchanges;

/// <summary>
///     The one way of the scanner's REST requests to the exchange (decision 7): at most <see cref="ScannerSettings.RestRequestsPerSecond" />
///     attempts a second, one attempt per request with the retries of a transient failure, and none at all for
///     <see cref="ScannerSettings.RestPause" /> after the exchange bans the IP, or for the growing <see cref="ScannerSettings.RestLimitPause" />
///     after it says the requests went over its limit. The bots share the IP, and a ban stops them.
/// </summary>
internal sealed class ScannerRestGate : IDisposable
{
    private readonly ScannerSettings _settings;
    private readonly ExchangeResilienceExecutor _resilience;
    private readonly IScannerStateRepository _state;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _tokens;
    private readonly ITimer _replenish;
    private long _pausedUntil = DateTimeOffset.MinValue.UtcTicks;
    private long _limitPause;
    private Task? _restored;

    public ScannerRestGate(IOptionsMonitor<ScannerSettings> monitor, ExchangeResilienceExecutor resilience, IScannerStateRepository state, TimeProvider time)
    {
        ScannerSettings settings = monitor.CurrentValue;
        _settings = settings;
        _resilience = resilience;
        _state = state;
        _time = time;

        // A token bucket on the clock of the scanner, so tests do not wait for real time: the bucket fills up again every second.
        // Only the timer adds tokens, and requests only take them, so a fill never goes over the limit. The limit is read at each
        // fill, so a new one holds from the next second; the bucket has no capacity of its own, or a higher limit would overfill it.
        _tokens = new SemaphoreSlim(settings.RestRequestsPerSecond);
        _replenish = time.CreateTimer(
            _ =>
            {
                int missing = monitor.CurrentValue.RestRequestsPerSecond - _tokens.CurrentCount;

                if (missing > 0)
                {
                    _tokens.Release(missing);
                }
            },
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
    }

    /// <exception cref="ExchangeRateLimitedException">The exchange said the requests went over its limit, now or before the pause ended.</exception>
    /// <exception cref="ExchangeUnavailableException">The retries of a transient failure ran out.</exception>
    public async Task<T> SendAsync<T>(Func<CancellationToken, ValueTask<T>> request, CancellationToken cancellationToken)
    {
        // The pause outlives the process that recorded it: the first request of this process takes it from the state; a read that
        // failed is tried again with the next request.
        Task restored = LazyInitializer.EnsureInitialized(ref _restored, () => RestoreAsync(cancellationToken));

        try
        {
            await restored;
        }
        catch
        {
            _ = Interlocked.CompareExchange(ref _restored, null, restored);

            throw;
        }

        var pausedUntil = new DateTimeOffset(Interlocked.Read(ref _pausedUntil), TimeSpan.Zero);

        if (_time.GetUtcNow() < pausedUntil)
        {
            throw new ExchangeRateLimitedException($"Bybit limited the requests of the scanner; they wait until {pausedUntil:O}.");
        }

        try
        {
            return await _resilience.ExecuteWithRetriesAsync(
                async token =>
                {
                    await _tokens.WaitAsync(token);

                    try
                    {
                        T result = await request(token);
                        Interlocked.Exchange(ref _limitPause, 0);

                        return result;
                    }
                    catch (TransientExchangeException exception) when (exception.RateLimited)
                    {
                        // Never retried: another attempt would hit the limit again.
                        await PauseAsync(exception.IpBanned ? _settings.RestPause : NextLimitPause(), cancellationToken);

                        throw new ExchangeRateLimitedException($"Bybit limited the requests of the scanner: {exception.Message}");
                    }
                },
                cancellationToken);
        }
        catch (TransientExchangeException exception)
        {
            throw exception.Unavailable($"Bybit request failed after {_resilience.RetryCount} retries: {exception.Message}");
        }
    }

    // The first answer about the limit pauses for RestLimitPause, each next one in a row twice as long, up to RestPause. The ladder
    // is kept in memory only: after a restart it starts again, while the recorded end of the pause still holds.
    private TimeSpan NextLimitPause()
    {
        long last = Interlocked.Read(ref _limitPause);
        long pause = Math.Min(last == 0 ? _settings.RestLimitPause.Ticks : last * 2, _settings.RestPause.Ticks);
        Interlocked.Exchange(ref _limitPause, pause);

        return TimeSpan.FromTicks(pause);
    }

    // A pause only ever ends later: a limit on a request sent before a ban of the IP comes back after it and must not shorten it.
    private async Task PauseAsync(TimeSpan pause, CancellationToken cancellationToken)
    {
        DateTimeOffset until = _time.GetUtcNow() + pause;
        long current;

        do
        {
            current = Interlocked.Read(ref _pausedUntil);
        }
        while (current < until.UtcTicks && Interlocked.CompareExchange(ref _pausedUntil, until.UtcTicks, current) != current);

        await _state.PauseRestAsync(until, cancellationToken);
    }

    private async Task RestoreAsync(CancellationToken cancellationToken)
    {
        if ((await _state.GetAsync(cancellationToken)).RestPausedUntil is { } until)
        {
            Interlocked.Exchange(ref _pausedUntil, until.UtcTicks);
        }
    }

    public void Dispose()
    {
        _replenish.Dispose();
        _tokens.Dispose();
    }
}