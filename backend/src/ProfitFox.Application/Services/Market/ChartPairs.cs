using System.Runtime.ExceptionServices;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Services.Market;

/// <summary>
///     The pairs of the requests of the chart of a pair and of its open interest, one for the process of the API (change
///     add-ideas-and-market, task 21.5): the whole list of instruments read for each request took more than half of the time
///     of a page of <c>5m</c> or <c>1h</c>. The list read is kept for <see cref="Keep" />; one read serves all, and the
///     requests that come while it is read wait for it. A pair the list kept does not have — listed since — reads the list
///     again. The scanner and «Рынок» read the catalog as before.
/// </summary>
/// <remarks>
///     Task 22.2: the read is shared and runs without the cancellation of the request that started it — a request cancelled
///     stops waiting, the read goes on for the others, and the limit of the page bounds it (task 23.2). A failed
///     read refuses all who wait for it at once and is remembered for <see cref="Remember" />: the requests in that time get
///     the same refusal without a new read, so a catalog that hangs does not hold the requests one after another.
///     Task 23.2: the read has the limit of the reads of the page (<see cref="PageReads.Limit" />), not the minute of the
///     timeout of the client; a read out of time and the timeout of the client — a <see cref="TaskCanceledException" /> while
///     no request was cancelled — are a refusal, <see cref="HistoryUnavailableException" />, which the page gets as 502.
/// </remarks>
public sealed class ChartPairs(IInstrumentCatalog catalog, TimeProvider time, PageReads? reads = null)
{
    /// <summary>
    ///     How long the list read is kept.
    /// </summary>
    public static readonly TimeSpan Keep = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     How long a failed read is remembered.
    /// </summary>
    public static readonly TimeSpan Remember = TimeSpan.FromSeconds(5);

    private readonly Lock _gate = new();
    private Failed? _failed;
    private volatile Listed? _listed;
    private readonly PageReads _reads = reads ?? new PageReads(time);
    private Task<Listed>? _reading;

    /// <summary>
    ///     The pair of <paramref name="symbol" />, or null when the catalog does not list it.
    /// </summary>
    /// <exception cref="HistoryUnavailableException">The catalog did not answer.</exception>
    public async Task<Instrument?> Find(string symbol, CancellationToken cancellation)
    {
        Listed? kept = _listed;
        if (kept is not null && time.GetUtcNow() - kept.Read < Keep && kept.Pairs.TryGetValue(symbol, out Instrument? pair))
        {
            return pair;
        }

        Listed listed = await ReadAfter(kept, symbol).WaitAsync(cancellation);
        return listed.Pairs.GetValueOrDefault(symbol);
    }

    // The list read after `seen`: the one read since, the read under way, a new read — or the failure remembered. The
    // symbol is that of the request that starts a read, for the journal.
    private Task<Listed> ReadAfter(Listed? seen, string symbol)
    {
        lock (_gate)
        {
            if (_listed is { } current && !ReferenceEquals(current, seen))
            {
                return Task.FromResult(current);
            }

            if (_reading is { } reading)
            {
                return reading;
            }

            if (_failed is { } failed && time.GetUtcNow() - failed.At < Remember)
            {
                return Task.FromException<Listed>(failed.Error.SourceException);
            }

            _reading = Read(symbol);
            return _reading;
        }
    }

    // One read of the list for all who wait for it; it ends by clearing itself and keeping the list or the failure.
    private async Task<Listed> Read(string symbol)
    {
        await Task.Yield();
        DateTimeOffset read = time.GetUtcNow();
        try
        {
            IReadOnlyList<Instrument> instruments = await Instruments(symbol);
            var listed = new Listed(read, instruments.GroupBy(pair => pair.Symbol, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal));
            lock (_gate)
            {
                _listed = listed;
                _failed = null;
                _reading = null;
            }

            return listed;
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                _failed = new Failed(time.GetUtcNow(), ExceptionDispatchInfo.Capture(exception));
                _reading = null;
            }

            throw;
        }
    }

    // The instruments under the limit of the page; out of time or cut by the timeout of the client, the catalog did not
    // answer — the read has no request to cancel it.
    private async Task<IReadOnlyList<Instrument>> Instruments(string symbol)
    {
        try
        {
            return await _reads.Run("catalog", symbol, catalog.LinearUsdtPerpetuals, CancellationToken.None);
        }
        catch (ReadTimedOutException exception)
        {
            throw new HistoryUnavailableException($"The instrument catalog of the market-data service did not answer in {(long)PageReads.Limit.TotalSeconds} s.", exception, dropped: true);
        }
        catch (OperationCanceledException exception)
        {
            throw new HistoryUnavailableException("The instrument catalog of the market-data service did not answer in the time of its client.", exception, dropped: true);
        }
    }

    // The list of instruments by symbol, and when it was read.
    private sealed record Listed(DateTimeOffset Read, IReadOnlyDictionary<string, Instrument> Pairs);

    // A failed read and when it failed.
    private sealed record Failed(DateTimeOffset At, ExceptionDispatchInfo Error);
}
