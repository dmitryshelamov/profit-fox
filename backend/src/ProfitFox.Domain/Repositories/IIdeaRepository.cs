using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Repositories;

/// <summary>Stores the live ideas; storage keeps one open idea per key (decision 5).</summary>
public interface IIdeaRepository
{
    /// <summary>Adds the ideas and returns the ones added; an idea whose key already has an open idea is refused by storage and left out.</summary>
    Task<IReadOnlyList<Idea>> AddAsync(IReadOnlyCollection<Idea> ideas, CancellationToken cancellationToken);

    /// <summary>Writes where the ideas stand: their levels, status, entry and exit.</summary>
    Task UpdateAsync(IReadOnlyCollection<Idea> ideas, CancellationToken cancellationToken);

    /// <summary>The ideas awaiting entry or active.</summary>
    Task<IReadOnlyList<Idea>> GetOpenAsync(CancellationToken cancellationToken);

    /// <summary>The open ideas and the ones closed since <paramref name="closedSince" />, of one pair or of all.</summary>
    Task<IReadOnlyList<Idea>> GetRecentAsync(DateTimeOffset closedSince, Instrument? instrument, CancellationToken cancellationToken);

    Task<Idea?> GetAsync(Guid id, CancellationToken cancellationToken);
}