using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Tests.Unit.Application.Services.MarketData;

/// <summary>
///     Months of history in memory, for the rules of the cache without the disk; reads and saves may be made to fail.
/// </summary>
internal sealed class MemoryHistoryMonthStore : IHistoryMonthStore
{
    private readonly Dictionary<HistoryMonth, object> _months = [];

    /// <summary>
    ///     The months saved, in the order of saving.
    /// </summary>
    public List<HistoryMonth> Saves { get; } = [];

    public bool FailReads { get; set; }

    public bool FailSaves { get; set; }

    public IReadOnlyCollection<HistoryMonth> Months
    {
        get
        {
            lock (_months)
            {
                return [.. _months.Keys];
            }
        }
    }

    public Task<HistoryRead<HistoryCandle>?> ReadCandles(HistoryMonth month, CancellationToken cancellation)
    {
        return Read<HistoryCandle>(month);
    }

    public Task<HistoryRead<HistoryPayment>?> ReadFunding(HistoryMonth month, CancellationToken cancellation)
    {
        return Read<HistoryPayment>(month);
    }

    public Task<HistoryRead<OpenInterestPoint>?> ReadOpenInterest(HistoryMonth month, CancellationToken cancellation)
    {
        return Read<OpenInterestPoint>(month);
    }

    public Task SaveOpenInterest(HistoryMonth month, HistoryRead<OpenInterestPoint> read, CancellationToken cancellation)
    {
        return Save(month, read);
    }

    public Task SaveCandles(HistoryMonth month, HistoryRead<HistoryCandle> read, CancellationToken cancellation)
    {
        return Save(month, read);
    }

    public Task SaveFunding(HistoryMonth month, HistoryRead<HistoryPayment> read, CancellationToken cancellation)
    {
        return Save(month, read);
    }

    private Task<HistoryRead<T>?> Read<T>(HistoryMonth month)
    {
        if (FailReads)
        {
            return Task.FromException<HistoryRead<T>?>(new IOException("the disk is gone"));
        }

        lock (_months)
        {
            return Task.FromResult(_months.TryGetValue(month, out object? read) ? (HistoryRead<T>?)read : null);
        }
    }

    private Task Save<T>(HistoryMonth month, HistoryRead<T> read)
    {
        if (FailSaves)
        {
            return Task.FromException(new UnauthorizedAccessException("the folder is not writable"));
        }

        lock (_months)
        {
            _months[month] = read;
            Saves.Add(month);
        }

        return Task.CompletedTask;
    }
}
