namespace ProfitFox.Bench.Presentation.Preview;

/// <summary>
///     The preview of a signal on a pair over exploration: the candles with trades, the covered windows, the events in time
///     order and the end of exploration, all times in Unix milliseconds UTC.
/// </summary>
public sealed record PreviewResponse(
    string Instrument,
    string Timeframe,
    IReadOnlyList<PreviewCandle> Candles,
    IReadOnlyList<PreviewRange> Coverage,
    IReadOnlyList<PreviewEvent> Events,
    long PeriodEnd);
