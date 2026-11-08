using ProfitFox.Domain.ValueObjects.Market;

namespace ProfitFox.Domain.Repositories;

/// <summary>Stores the breaks of the streams whose missed events cannot be loaded again.</summary>
public interface IStreamBreakRepository
{
    Task AddAsync(StreamBreak streamBreak, CancellationToken cancellationToken);
}