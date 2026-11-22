namespace ProfitFox.Presentation.Contracts.Preview;

/// <summary>
///     The preview of a strategy on a pair over exploration: the candles with trades, the covered windows, the orders in time
///     order with their outcomes, the cycles in the order they opened, the end of exploration and, when the cycles are
///     computed only for the last weeks, the moment they are computed from; all times in Unix milliseconds UTC.
/// </summary>
public sealed record StrategyPreviewResponse(
    string Instrument,
    string Timeframe,
    IReadOnlyList<PreviewCandle> Candles,
    IReadOnlyList<PreviewRange> Coverage,
    IReadOnlyList<PreviewOrder> Orders,
    IReadOnlyList<PreviewCycle> Cycles,
    long PeriodEnd,
    long? ComputedFrom);
