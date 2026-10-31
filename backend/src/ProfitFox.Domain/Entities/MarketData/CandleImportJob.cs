using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Entities.MarketData;

/// <summary>A request to load candles of one instrument and interval for <c>[From, To)</c>, executed in the background.</summary>
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
    DateTimeOffset? FinishedAt);
