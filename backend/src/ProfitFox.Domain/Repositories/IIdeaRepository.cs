using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Repositories;

/// <summary>Stores the live ideas; storage keeps one open idea per key (decision 5).</summary>
public interface IIdeaRepository
{
    /// <summary>
    ///     In one transaction adds the ideas made and writes where the moved ones stand (their levels, status, entry and exit); returns the ideas
    ///     added. An idea whose key already has an open idea is refused by storage and left out.
    /// </summary>
    Task<IReadOnlyList<Idea>> SaveAsync(IReadOnlyCollection<Idea> created, IReadOnlyCollection<Idea> changed, CancellationToken cancellationToken);

    /// <summary>The ideas awaiting entry or active.</summary>
    Task<IReadOnlyList<Idea>> GetOpenAsync(CancellationToken cancellationToken);

    /// <summary>The open ideas and the ones closed since <paramref name="closedSince" />, of one pair or of all.</summary>
    Task<IReadOnlyList<Idea>> GetRecentAsync(DateTimeOffset closedSince, Instrument? instrument, CancellationToken cancellationToken);

    Task<Idea?> GetAsync(Guid id, CancellationToken cancellationToken);
}