using Microsoft.Extensions.Logging;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;

namespace ProfitFox.Application.Services.Market;

/// <summary>
///     The reads of the page of a pair under a limit of their own, one for the process (change add-ideas-and-market, task
///     23.2): the catalog, the candles, the payments, and the open interest from History and from the REST, each attempt
///     <see cref="Limit" /> at most. Live, one read of the open interest took 62 seconds — the timeout of the client — and
///     the panel stood empty for a minute. The clients are those of the scanner and the backtests, with their timeouts; the
///     limit is the page's only. A step longer than <see cref="Slow" /> and every limit expired are a line of the journal of
///     level <c>Warning</c> with the step, the pair and the time, no more than once in <see cref="WarningEvery" /> for each
///     step — the slow steps and the steps out of time each. A request cancelled — the page went — is not out of time and is
///     not written.
/// </summary>
public sealed class PageReads(TimeProvider time, ILogger<PageReads>? logger = null)
{
    /// <summary>
    ///     How long one attempt of a read of the page may take.
    /// </summary>
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     A step longer than this is written to the journal.
    /// </summary>
    public static readonly TimeSpan Slow = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     How often the journal tells of the same step.
    /// </summary>
    public static readonly TimeSpan WarningEvery = TimeSpan.FromMinutes(1);

    private readonly Dictionary<string, DateTimeOffset> _warned = new(StringComparer.Ordinal);
    private readonly Lock _warnedLock = new();

    /// <summary>
    ///     The steps of the reads of the page of <paramref name="symbol" />, for a client that reads in several attempts.
    /// </summary>
    public IReadSteps Of(string symbol)
    {
        return new Steps(this, symbol);
    }

    /// <summary>
    ///     Runs one attempt of <paramref name="step" /> for the page of <paramref name="symbol" />: its token is cancelled when
    ///     the request is or the limit expires, and the answer comes by the limit even from an attempt that does not heed it.
    /// </summary>
    /// <exception cref="ReadTimedOutException">The attempt did not end within <see cref="Limit" />.</exception>
    /// <exception cref="OperationCanceledException">The request was cancelled.</exception>
    public async Task<T> Run<T>(string step, string symbol, Func<CancellationToken, Task<T>> attempt, CancellationToken cancellation)
    {
        using var limit = new CancellationTokenSource(Limit, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, limit.Token);
        long started = time.GetTimestamp();
        try
        {
            T result = await attempt(linked.Token).WaitAsync(linked.Token);
            WarnIfSlow(step, symbol, time.GetElapsedTime(started));
            return result;
        }
        catch (Exception exception) when (limit.IsCancellationRequested && !cancellation.IsCancellationRequested)
        {
            TimeSpan took = time.GetElapsedTime(started);
            if (Due($"{step} out of time"))
            {
                logger?.LogWarning(
                    "The read of {Step} of the page of {Symbol} did not end in {Limit} s ({Took} ms): the page is answered that it loads, a catalog out of time with 502; the same step is not written again for a minute.",
                    step,
                    symbol,
                    (long)Limit.TotalSeconds,
                    (long)took.TotalMilliseconds);
            }

            throw new ReadTimedOutException(step, Limit, exception);
        }
        catch (Exception) when (!cancellation.IsCancellationRequested)
        {
            WarnIfSlow(step, symbol, time.GetElapsedTime(started));
            throw;
        }
    }

    private void WarnIfSlow(string step, string symbol, TimeSpan took)
    {
        if (took > Slow && Due(step))
        {
            logger?.LogWarning(
                "The read of {Step} of the page of {Symbol} took {Took} ms, longer than {Slow} s; the same step is not written again for a minute.",
                step,
                symbol,
                (long)took.TotalMilliseconds,
                (long)Slow.TotalSeconds);
        }
    }

    // Whether the journal may tell of `key` now: not within a minute of the last time it did.
    private bool Due(string key)
    {
        if (logger is null)
        {
            return false;
        }

        DateTimeOffset now = time.GetUtcNow();
        lock (_warnedLock)
        {
            if (_warned.TryGetValue(key, out DateTimeOffset last) && now - last < WarningEvery)
            {
                return false;
            }

            _warned[key] = now;
            return true;
        }
    }

    // The steps of the page of one pair.
    private sealed class Steps(PageReads reads, string symbol) : IReadSteps
    {
        public Task<T> Run<T>(string step, Func<CancellationToken, Task<T>> attempt, CancellationToken cancellation)
        {
            return reads.Run(step, symbol, attempt, cancellation);
        }
    }
}
