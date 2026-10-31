using ProfitFox.Domain.Entities.MarketData;

namespace ProfitFox.Domain.Repositories;

public interface ICandleImportJobRepository
{
    Task AddAsync(CandleImportJob job, CancellationToken cancellationToken);

    Task<CandleImportJob?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Saves the job's status, progress, error and start/finish times.</summary>
    Task UpdateAsync(CandleImportJob job, CancellationToken cancellationToken);

    /// <summary>Moves every <c>Pending</c> and <c>Running</c> job to <c>Failed</c>.</summary>
    Task FailUnfinishedAsync(string reason, DateTimeOffset now, CancellationToken cancellationToken);
}