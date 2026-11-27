using System.Diagnostics;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Commands.Backtests.ExecuteBacktest;

/// <summary>
///     The source of history of a backtest that counts how long its reads took: the time of reading of the backtest; the
///     rest of its time is its calculation.
/// </summary>
internal sealed class TimedHistorySource(IHistorySource source) : IHistorySource
{
    private long _ticks;

    public TimeSpan Elapsed => TimeSpan.FromTicks(Interlocked.Read(ref _ticks));

    public async Task<HistoryRead<HistoryCandle>> ReadCandles(string instrumentId, TimeSpan interval, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            return await source.ReadCandles(instrumentId, interval, from, to, cancellation);
        }
        finally
        {
            Interlocked.Add(ref _ticks, Stopwatch.GetElapsedTime(start).Ticks);
        }
    }

    public async Task<HistoryRead<HistoryPayment>> ReadFunding(string instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            return await source.ReadFunding(instrumentId, from, to, cancellation);
        }
        finally
        {
            Interlocked.Add(ref _ticks, Stopwatch.GetElapsedTime(start).Ticks);
        }
    }

    public async Task<HistoryRead<OpenInterestPoint>> ReadOpenInterest(string instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            return await source.ReadOpenInterest(instrumentId, from, to, cancellation);
        }
        finally
        {
            Interlocked.Add(ref _ticks, Stopwatch.GetElapsedTime(start).Ticks);
        }
    }
}
