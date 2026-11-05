using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Repositories;

/// <summary>Stores the percentiles of volatility of the pairs, which the replay of history computes once a day.</summary>
public interface IIdeaVolatilityRepository
{
    Task<IReadOnlyList<IdeaVolatility>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Inserts the percentiles or replaces the stored ones of the same pair and interval.</summary>
    Task SaveAsync(IReadOnlyCollection<IdeaVolatility> percentiles, CancellationToken cancellationToken);
}