using ProfitFox.Domain.Entities.Filters;

namespace ProfitFox.Domain.Repositories;

/// <summary>
///     Where the saved filters are kept (change add-ideas-and-market, design.md, decision 11); a name is one of its kind.
/// </summary>
public interface ISavedFilterRepository
{
    /// <summary>
    ///     Every filter, by name.
    /// </summary>
    Task<IReadOnlyList<SavedFilter>> AllAsync(CancellationToken cancellationToken);

    Task<SavedFilter?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    ///     Writes a new filter or replaces the one of its id; false when another filter has the name.
    /// </summary>
    Task<bool> SaveAsync(SavedFilter filter, CancellationToken cancellationToken);

    /// <summary>
    ///     Removes the filter; false when there was none.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
