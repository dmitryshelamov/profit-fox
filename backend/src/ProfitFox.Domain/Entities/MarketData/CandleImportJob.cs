using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Entities.MarketData;

/// <summary>
///     A request to load candles of one instrument and interval for <c>[From, To)</c>, executed in the background.
///     <see cref="InterruptionsWithoutProgress" /> counts restarts in a row that stopped the running job before it stored a candle.
/// </summary>
public sealed record CandleImportJob(
    Guid Id,
    Instrument Instrument,
    Interval Interval,
    DateTimeOffset From,
    DateTimeOffset To,
    CandleImportStatus Status,
    long ImportedCandles,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    int InterruptionsWithoutProgress = 0);