using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.IdeaAutos.Enums;

namespace ProfitFox.Domain.Repositories;

/// <summary>Stores the autos. A deleted auto stays in storage for the names of its trades and refusals and drops out of the list.</summary>
public interface IIdeaAutoRepository
{
    /// <summary>The autos that are not deleted, by their place in the list.</summary>
    Task<IReadOnlyList<IdeaAuto>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>The autos that are not deleted and not off, by their place in the list.</summary>
    Task<IReadOnlyList<IdeaAuto>> GetEnabledAsync(CancellationToken cancellationToken);

    /// <summary>Every auto there is, the deleted ones too.</summary>
    Task<IReadOnlyDictionary<Guid, IdeaAuto>> GetEveryAsync(CancellationToken cancellationToken);

    /// <summary>An auto by its id, a deleted one too; none for an id there is not.</summary>
    Task<IdeaAuto?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Adds the auto at the end of the list; returns it with its place.</summary>
    Task<IdeaAuto> CreateAsync(IdeaAuto auto, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Writes the name, the account, the amount and the conditions of an auto that is not deleted; <c>false</c> when there is none.</summary>
    Task<bool> SaveAsync(IdeaAuto auto, CancellationToken cancellationToken);

    Task<bool> SetModeAsync(Guid id, IdeaAutoMode mode, CancellationToken cancellationToken);

    /// <summary>Gives the autos the places of their ids in the list.</summary>
    Task ReorderAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Marks an auto deleted; <c>false</c> when there is none that is not deleted.</summary>
    Task<bool> DeleteAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken);
}