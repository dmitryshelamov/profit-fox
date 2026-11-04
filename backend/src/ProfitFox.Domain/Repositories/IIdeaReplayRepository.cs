using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Repositories;

/// <summary>Stores how far the replay of history has gone for each pair, per version of the rules.</summary>
public interface IIdeaReplayRepository
{
    Task<IReadOnlyDictionary<Instrument, DateTimeOffset>> GetAsync(int engineVersion, CancellationToken cancellationToken);

    Task SetAsync(Instrument instrument, int engineVersion, DateTimeOffset replayedUntil, CancellationToken cancellationToken);

    /// <summary>Removes the cursors of every other version.</summary>
    Task DeleteOtherVersionsAsync(int engineVersion, CancellationToken cancellationToken);
}