using ProfitFox.Bench.MarketData;
using ProfitFox.Bench.MarketData.History;

namespace ProfitFox.Bench.Runs;

/// <summary>
///     What a run read and found for one pair: for the parity dump.
/// </summary>
public sealed record PairData(
    Instrument Instrument,
    HistoryRead<HistoryCandle> SignalCandles,
    HistoryRead<HistoryPayment> Funding,
    IReadOnlyList<(DateTimeOffset From, DateTimeOffset To, HistoryRead<HistoryCandle> Minutes)> Spans,
    IReadOnlyList<EventRow> Rows);
