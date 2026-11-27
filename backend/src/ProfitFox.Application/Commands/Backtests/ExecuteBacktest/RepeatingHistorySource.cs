using Microsoft.Extensions.Logging;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Commands.Backtests.ExecuteBacktest;

/// <summary>
///     The source of history of a backtest that repeats a read the market-data service dropped (task 12.12), after the pauses
///     of <see cref="HistoryRepeats" /> on the clock of the bench. A repeat reads the window from scratch: nothing of the broken
///     read is kept, so the history and its hash are those of a read that was not dropped. A refused window and a cancelled
///     read are not repeated; when the repeats are used up the last error goes on.
/// </summary>
internal sealed class RepeatingHistorySource(IHistorySource source, HistoryRepeats repeats, TimeProvider time, ILogger logger) : IHistorySource
{
    public Task<HistoryRead<HistoryCandle>> ReadCandles(string instrumentId, TimeSpan interval, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        return Repeat(instrumentId, () => source.ReadCandles(instrumentId, interval, from, to, cancellation), cancellation);
    }

    public Task<HistoryRead<HistoryPayment>> ReadFunding(string instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        return Repeat(instrumentId, () => source.ReadFunding(instrumentId, from, to, cancellation), cancellation);
    }

    public Task<HistoryRead<OpenInterestPoint>> ReadOpenInterest(string instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        return Repeat(instrumentId, () => source.ReadOpenInterest(instrumentId, from, to, cancellation), cancellation);
    }

    private async Task<T> Repeat<T>(string instrumentId, Func<Task<T>> read, CancellationToken cancellation)
    {
        for (int repeat = 0; ; repeat++)
        {
            try
            {
                return await read();
            }
            catch (HistoryUnavailableException exception) when (exception.Dropped && repeat < repeats.Pauses.Count && !cancellation.IsCancellationRequested)
            {
                TimeSpan pause = repeats.Pauses[repeat];
                logger.LogWarning(
                    "{Line}",
                    $"The read of {instrumentId} was dropped ({exception.Message}); repeat {repeat + 1} of {repeats.Pauses.Count} in {pause.TotalSeconds:F0} s");
                await Task.Delay(pause, time, cancellation);
            }
            catch (HistoryUnavailableException exception) when (exception.Dropped && repeats.Pauses.Count > 0 && !cancellation.IsCancellationRequested)
            {
                throw new HistoryUnavailableException($"{exception.Message} (repeated {repeats.Pauses.Count} times)", exception, dropped: true);
            }
        }
    }
}
