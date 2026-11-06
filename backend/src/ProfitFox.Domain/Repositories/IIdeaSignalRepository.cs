using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Repositories;

/// <summary>Stores the signals of the replay of history with their paths (decisions 1 and 4), in <c>idea_cases</c>.</summary>
public interface IIdeaSignalRepository
{
    /// <summary>Writes the signals; a signal already stored under its key, fired time and version takes the path it has gone on to.</summary>
    Task UpsertAsync(IReadOnlyCollection<IdeaSignal> signals, CancellationToken cancellationToken);

    /// <summary>
    ///     The signals of a version fired since <paramref name="from" />, of one type × interval or of all, in the order of their key and firing,
    ///     read one by one.
    /// </summary>
    IAsyncEnumerable<IdeaSignal> StreamAsync(int engineVersion, DateTimeOffset from, (IdeaType Type, Interval Interval)? only, CancellationToken cancellationToken);

    /// <summary>The signals of a pair of a version, oldest fired first.</summary>
    Task<IReadOnlyList<IdeaSignal>> GetAsync(Instrument instrument, int engineVersion, CancellationToken cancellationToken);

    /// <summary>Removes the signals and cases of every other version.</summary>
    Task DeleteOtherVersionsAsync(int engineVersion, CancellationToken cancellationToken);
}