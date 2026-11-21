namespace ProfitFox.Presentation.Contracts.Preview;

/// <summary>
///     The preview of a strategy on a pair over exploration: the candles with trades, the covered windows, the orders in time
///     order with their outcomes, the end of exploration and, when the trades are computed only for the last weeks, the start
///     of the first computed week; all times in Unix milliseconds UTC.
/// </summary>
public sealed record StrategyPreviewResponse(
    string Instrument,
    string Timeframe,
    IReadOnlyList<PreviewCandle> Candles,
    IReadOnlyList<PreviewRange> Coverage,
    IReadOnlyList<PreviewOrder> Orders,
    long PeriodEnd,
    long? ComputedFrom);
