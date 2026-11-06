using ProfitFox.Domain.Entities.IdeaAutos;

namespace ProfitFox.Domain.Repositories;

/// <summary>The refusals of the autos, one per idea and auto.</summary>
public interface IIdeaAutoRefusalRepository
{
    /// <summary>Writes the refusal in the unit of work; one the idea and the auto have already is left as it is.</summary>
    Task AddAsync(IdeaAutoRefusal refusal, CancellationToken cancellationToken);

    /// <summary>The refusals since <paramref name="since" /> from the newest, of one auto or of all, from the row <paramref name="skip" />, and how many there are.</summary>
    Task<(IReadOnlyList<IdeaAutoRefusal> Items, int Total)> GetPageAsync(DateTimeOffset since, Guid? autoId, int skip, int take, CancellationToken cancellationToken);

    /// <summary>The number of refusals since <paramref name="since" /> of each auto that has any.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Deletes the refusals made before <paramref name="before" />; returns how many.</summary>
    Task<int> DeleteBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken);
}