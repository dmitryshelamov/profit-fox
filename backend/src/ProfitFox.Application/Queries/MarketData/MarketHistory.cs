using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Services.MarketData;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Queries.MarketData;

/// <summary>
///     History as a run sees it (spec market-history): read straight from the market-data service, never after the end
///     of the period and never after the start of the current UTC day, which the service does not hold yet, with every read
///     added to the hash of the run. The current day is the one of the moment the history is made: a run that goes across
///     midnight UTC reads every pair alike (task 12.13).
/// </summary>
public sealed class MarketHistory
{
    private readonly IHistorySource _source;
    private readonly DateTimeOffset _readUntil;
    private readonly HistoryHash _hash;
    private readonly DateTimeOffset _today;

    /// <param name="source">Where the history is read from.</param>
    /// <param name="readUntil">The end of the period of the run; nothing after it is read.</param>
    /// <param name="hash">The hash of the run every read is added to.</param>
    /// <param name="time">The clock that gives the current UTC day, taken once here; nothing of that day is read.</param>
    public MarketHistory(IHistorySource source, DateTimeOffset readUntil, HistoryHash hash, TimeProvider time)
    {
        _source = source;
        _readUntil = readUntil;
        _hash = hash;
        _today = HistoryPeriod.Today(time.GetUtcNow());
    }

    /// <summary>
    ///     Candles of <paramref name="interval" /> opened in [<paramref name="from" />, <paramref name="to" />) and the
    ///     covered parts of that window.
    /// </summary>
    public async Task<HistoryRead<HistoryCandle>> Candles(
        string instrumentId,
        TimeSpan interval,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellation)
    {
        CheckWindow(from, to);
        HistoryRead<HistoryCandle> read = await _source.ReadCandles(instrumentId, interval, from, to, cancellation);
        read = read with { Coverage = Coverage.Trim(read.Coverage, from, to) };
        _hash.Add(instrumentId, interval, read);
        return read;
    }

    /// <summary>
    ///     The candles of the signal: only those with trades go to the executor (spec market-history).
    /// </summary>
    public async Task<(IReadOnlyList<TimeRange> Coverage, IReadOnlyList<ClosedCandle> Candles)> TradedCandles(
        string instrumentId,
        TimeSpan interval,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellation)
    {
        HistoryRead<HistoryCandle> read = await Candles(instrumentId, interval, from, to, cancellation);
        return (read.Coverage, [.. read.Items.Where(candle => candle.HasTrades).Select(candle => candle.ToClosedCandle(interval))]);
    }

    /// <summary>
    ///     Funding payments at times in [<paramref name="from" />, <paramref name="to" />) and the covered parts of that window.
    /// </summary>
    public async Task<HistoryRead<HistoryPayment>> Funding(string instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        CheckWindow(from, to);
        HistoryRead<HistoryPayment> read = await _source.ReadFunding(instrumentId, from, to, cancellation);
        read = read with { Coverage = Coverage.Trim(read.Coverage, from, to) };
        _hash.Add(instrumentId, read);
        return read;
    }

    /// <summary>
    ///     Funding payments at times in [<paramref name="from" />, <paramref name="through" />]: a payment exactly at the end
    ///     of the period belongs to the trades that end there. The coverage reaches past <paramref name="through" /> when the
    ///     payment there is known. When <paramref name="through" /> is the start of the current UTC day the payment there is
    ///     not read: the service holds past days only, so the coverage ends at <paramref name="through" /> and a trade that
    ///     ends there is left out on funding (task 12.1).
    /// </summary>
    public async Task<HistoryRead<HistoryPayment>> FundingThrough(
        string instrumentId,
        DateTimeOffset from,
        DateTimeOffset through,
        CancellationToken cancellation)
    {
        CheckWindow(from, through);
        DateTimeOffset to = through < _today ? through + TimeSpan.FromSeconds(1) : through;
        HistoryRead<HistoryPayment> read = await _source.ReadFunding(instrumentId, from, to, cancellation);
        read = read with { Coverage = Coverage.Trim(read.Coverage, from, to) };
        _hash.Add(instrumentId, read);
        return read;
    }

    private void CheckWindow(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from)
        {
            throw new ArgumentException($"The window ends at {to:O}, not after its start {from:O}.");
        }

        if (to > _readUntil)
        {
            throw new PeriodBoundaryException($"The window ends at {to:O}, after the end of the period {_readUntil:O}.");
        }

        if (to > _today)
        {
            throw new PeriodBoundaryException($"The window ends at {to:O}, after the start of the current UTC day {_today:O}.");
        }
    }
}
