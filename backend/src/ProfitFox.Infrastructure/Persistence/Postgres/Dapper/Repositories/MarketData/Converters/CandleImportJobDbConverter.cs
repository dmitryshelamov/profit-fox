using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData.Converters;

internal static class CandleImportJobDbConverter
{
    internal static CandleImportJobDb FromDomain(CandleImportJob job) => new()
    {
        Id = job.Id,
        Category = job.Instrument.Category.ToCode(),
        Symbol = job.Instrument.Symbol,
        Interval = job.Interval.ToCode(),
        From = job.From,
        To = job.To,
        Status = job.Status.ToString(),
        ImportedCandles = job.ImportedCandles,
        Error = job.Error,
        CreatedAt = job.CreatedAt,
        StartedAt = job.StartedAt,
        FinishedAt = job.FinishedAt,
    };

    internal static CandleImportJob ToDomain(CandleImportJobDb job)
    {
        MarketDataCodes.TryParseCategory(job.Category, out var category);
        MarketDataCodes.TryParseInterval(job.Interval, out var interval);
        return new CandleImportJob(
            job.Id,
            new Instrument(category, job.Symbol),
            interval,
            job.From,
            job.To,
            Enum.Parse<CandleImportStatus>(job.Status),
            job.ImportedCandles,
            job.Error,
            job.CreatedAt,
            job.StartedAt,
            job.FinishedAt);
    }
}
