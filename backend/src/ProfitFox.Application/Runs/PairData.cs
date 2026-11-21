using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Runs;

namespace ProfitFox.Application.Runs;

/// <summary>
///     What a run read and found for one pair: for the parity dump. <see cref="Skipped" /> counts the events that came
///     while the position of the pair was open; they have no rows.
/// </summary>
public sealed record PairData(
    Instrument Instrument,
    HistoryRead<HistoryCandle> SignalCandles,
    HistoryRead<HistoryPayment> Funding,
    IReadOnlyList<(DateTimeOffset From, DateTimeOffset To, HistoryRead<HistoryCandle> Minutes)> Spans,
    IReadOnlyList<EventRow> Rows,
    int Skipped);
