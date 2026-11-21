using ProfitFox.Application.Contracts.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.Contracts.Preview;

/// <summary>
///     The preview of a strategy on a pair over exploration (spec strategy-preview): the candles with trades, the covered
///     windows, the entry orders in time order with what became of each, the end of exploration and, when the trades are
///     computed only for the last weeks, the start of the first computed week.
/// </summary>
public sealed record StrategyPreviewInternal(
    InstrumentInternal Instrument,
    TimeSpan Timeframe,
    IReadOnlyList<HistoryCandle> Candles,
    IReadOnlyList<TimeRange> Coverage,
    IReadOnlyList<OrderOutcome> Orders,
    DateTimeOffset PeriodEnd,
    DateTimeOffset? ComputedFrom);
