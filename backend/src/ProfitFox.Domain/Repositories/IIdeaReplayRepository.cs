using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Repositories;

/// <summary>Stores how far the replay of history has gone for each pair and its open ideas there, per version of the rules.</summary>
public interface IIdeaReplayRepository
{
    Task<IReadOnlyDictionary<Instrument, IdeaReplayCursor>> GetAsync(int engineVersion, CancellationToken cancellationToken);

    Task SetAsync(Instrument instrument, int engineVersion, IdeaReplayCursor cursor, CancellationToken cancellationToken);

    /// <summary>Removes the cursors of every other version.</summary>
    Task DeleteOtherVersionsAsync(int engineVersion, CancellationToken cancellationToken);
}