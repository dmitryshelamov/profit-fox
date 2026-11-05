using ProfitFox.Domain.Entities.SavedFilters;

namespace ProfitFox.Domain.Repositories;

public interface ISavedFilterRepository
{
    /// <summary>Every saved filter, oldest first.</summary>
    Task<IReadOnlyList<SavedFilter>> GetAllAsync(CancellationToken cancellationToken);

    Task<SavedFilter?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task AddAsync(SavedFilter filter, CancellationToken cancellationToken);

    /// <summary>Replaces the name, place and conditions; <c>false</c> when there is no such filter.</summary>
    Task<bool> UpdateAsync(SavedFilter filter, CancellationToken cancellationToken);

    /// <summary><c>false</c> when there is no such filter.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}