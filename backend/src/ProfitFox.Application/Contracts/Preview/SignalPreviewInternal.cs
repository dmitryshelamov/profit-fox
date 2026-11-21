using ProfitFox.Application.Contracts.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Contracts.Preview;

/// <summary>
///     The preview of a signal on a pair over exploration: the candles with trades, the covered windows, the moments of the
///     events in time order and the end of exploration.
/// </summary>
public sealed record SignalPreviewInternal(
    InstrumentInternal Instrument,
    TimeSpan Timeframe,
    IReadOnlyList<HistoryCandle> Candles,
    IReadOnlyList<TimeRange> Coverage,
    IReadOnlyList<DateTimeOffset> Events,
    DateTimeOffset PeriodEnd);
