using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.MarketData;

namespace ProfitFox.Presentation.Contracts.MarketData;

public sealed record CandleImportJobResponse(
    Guid Id,
    string Category,
    string Symbol,
    string Interval,
    DateTimeOffset From,
    DateTimeOffset To,
    string Status,
    long ImportedCandles,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt)
{
    public static CandleImportJobResponse Of(CandleImportJob job)
    {
        return new CandleImportJobResponse(
            job.Id,
            job.Instrument.Category.ToCode(),
            job.Instrument.Symbol,
            job.Interval.ToCode(),
            job.From,
            job.To,
            job.Status.ToString(),
            job.ImportedCandles,
            job.Error,
            job.CreatedAt,
            job.StartedAt,
            job.FinishedAt);
    }
}