using ProfitFox.Application.Contracts.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Positions;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.Contracts.Preview;

/// <summary>
///     The preview of a strategy on a pair over exploration (spec strategy-preview): the candles with trades, the covered
///     windows, the orders in time order with what became of each, the cycles in the order they opened, the end of
///     exploration and, when the cycles are computed only for the last weeks, the moment they are computed from.
/// </summary>
public sealed record StrategyPreviewInternal(
    InstrumentInternal Instrument,
    TimeSpan Timeframe,
    IReadOnlyList<HistoryCandle> Candles,
    IReadOnlyList<TimeRange> Coverage,
    IReadOnlyList<OrderOutcome> Orders,
    IReadOnlyList<CycleOutcome> Cycles,
    DateTimeOffset PeriodEnd,
    DateTimeOffset? ComputedFrom);
