using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Repositories;

/// <summary>Stores how far the replay of history has gone for each pair and its open ideas there, per version of the rules.</summary>
public interface IIdeaReplayRepository
{
    Task<IReadOnlyDictionary<Instrument, IdeaReplayCursor>> GetAsync(int engineVersion, CancellationToken cancellationToken);

    Task SetAsync(Instrument instrument, int engineVersion, IdeaReplayCursor cursor, CancellationToken cancellationToken);

    /// <summary>
    ///     How far the replay of <paramref name="engineVersion" /> has gone over the pairs it takes: listed, with their history loaded, their
    ///     coverage and their price step, as the choice of a turn takes them.
    /// </summary>
    Task<IdeaReplayProgress> ProgressAsync(int engineVersion, CancellationToken cancellationToken);

    /// <summary>Removes the cursors of every other version.</summary>
    Task DeleteOtherVersionsAsync(int engineVersion, CancellationToken cancellationToken);
}