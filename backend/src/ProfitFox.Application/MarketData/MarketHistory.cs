using ProfitFox.Domain.MarketData;
using ProfitFox.Domain.Execution;

namespace ProfitFox.Application.MarketData;

/// <summary>
///     History as a run sees it (spec market-history): read straight from the market-data service, never after the end
///     of the period, with every read added to the hash of the run.
/// </summary>
public sealed class MarketHistory
{
    private readonly IHistorySource _source;
    private readonly DateTimeOffset _readUntil;
    private readonly HistoryHash _hash;

    /// <param name="readUntil">The end of the period of the run; nothing after it is read.</param>
    public MarketHistory(IHistorySource source, DateTimeOffset readUntil, HistoryHash hash)
    {
        _source = source;
        _readUntil = readUntil;
        _hash = hash;
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
    ///     payment there is known.
    /// </summary>
    public async Task<HistoryRead<HistoryPayment>> FundingThrough(
        string instrumentId,
        DateTimeOffset from,
        DateTimeOffset through,
        CancellationToken cancellation)
    {
        CheckWindow(from, through);
        DateTimeOffset to = through + TimeSpan.FromSeconds(1);
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
    }
}
