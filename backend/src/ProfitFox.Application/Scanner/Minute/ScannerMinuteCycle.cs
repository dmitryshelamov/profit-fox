using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.Market;
using ProfitFox.Application.Scanner.Ideas;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Scanner.Minute;

/// <summary>
///     The minute of the scanner (decision 9): right after the end of a minute the tickers of each category, in one request each, and the
///     point of open interest at the start of a 5 minute step when the read falls in its first minute (it never replaces a stored point); <see cref="ScannerSettings.SnapshotDelay" /> later, so that the closed candles of the
///     minute have come, the snapshot of every pair, which replaces the previous one if this scanner still holds the stream. Then the ideas of
///     the minute and the setups of the pairs into their rows; a failure of the ideas is logged and leaves the snapshot as it is.
/// </summary>
public sealed class ScannerMinuteCycle(
    IMarketDataFeed feed,
    IOpenInterestRepository openInterest,
    IMarketSnapshotRepository snapshots,
    IScannerPairRepository pairs,
    ScannerMarket market,
    LiveIdeas ideas,
    ScannerPace pace,
    IScannerJournal journal,
    ScannerSettings settings,
    WorkerIdentity identity,
    TimeProvider time)
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Step = TimeSpan.FromMinutes(5);

    /// <summary>Runs the next minute and returns the pairs whose coverage lags behind it, to be caught up.</summary>
    /// <exception cref="ScannerLeaseLostException">Another scanner holds the stream now.</exception>
    public async Task<IReadOnlyList<Instrument>> RunMinuteAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        DateTimeOffset end = Floor(now, Minute) + Minute;
        await Task.Delay(end - now, time, cancellationToken);
        await ReadTickersAsync(MarketCategory.Spot, cancellationToken);
        await ReadTickersAsync(MarketCategory.Linear, cancellationToken);

        TimeSpan wait = end + settings.SnapshotDelay - time.GetUtcNow();

        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, time, cancellationToken);
        }

        DateTimeOffset computedAt = time.GetUtcNow();

        if (!await snapshots.ReplaceAsync(identity.Id, market.Snapshot(end, computedAt), computedAt, cancellationToken))
        {
            throw new ScannerLeaseLostException();
        }

        pace.Snapshot(end + settings.SnapshotDelay, time.GetUtcNow());
        await pairs.SaveAsync(market.TakeChanged(), cancellationToken);
        IReadOnlyDictionary<Instrument, IReadOnlyList<PairSetup>>? setups = null;

        try
        {
            setups = await ideas.RunAsync(end, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            journal.Failed("ideas", exception);
        }

        if (setups is not null && !await snapshots.ReplaceSetupsAsync(identity.Id, setups, cancellationToken))
        {
            throw new ScannerLeaseLostException();
        }

        return market.Lagging(end);
    }

    /// <summary>The snapshot goes on with the last tickers while the exchange does not answer.</summary>
    private async Task ReadTickersAsync(MarketCategory category, CancellationToken cancellationToken)
    {
        IReadOnlyList<MarketTicker> tickers;

        try
        {
            tickers = await feed.GetTickersAsync(category, cancellationToken);
        }
        catch (ExchangeUnavailableException)
        {
            return;
        }

        foreach (MarketTicker ticker in tickers)
        {
            market.Ticker(ticker);
        }

        DateTimeOffset now = time.GetUtcNow();
        DateTimeOffset step = Floor(now, Step);

        // Only a read in the first minute of a step tells the open interest of its start; a later one leaves the step to the history.
        if (category == MarketCategory.Linear && now - step < Minute)
        {
            var kept = market.Listed().Select(pair => pair.Instrument).ToHashSet();

            foreach (MarketTicker ticker in tickers.Where(ticker => kept.Contains(ticker.Instrument)))
            {
                var point = new OpenInterestPoint(step, ticker.OpenInterest!.Value);
                await openInterest.AddAsync(ticker.Instrument, [point], cancellationToken);
                market.OpenInterestPoint(ticker.Instrument, point);
            }
        }
    }

    private static DateTimeOffset Floor(DateTimeOffset time, TimeSpan unit)
    {
        return time.AddTicks(-(time.UtcTicks % unit.Ticks));
    }
}