using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.ValueObjects.IdeaAutos;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
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

    /// <summary>
    ///     The same, and in the same transaction writes how the ideas that closed ended on the trades made of them that have no outcome yet. It
    ///     touches nothing else of a trade, not its time of update, so no Worker rereads it.
    /// </summary>
    Task<IReadOnlyList<Idea>> SaveAsync(
        IReadOnlyCollection<Idea> created,
        IReadOnlyCollection<Idea> changed,
        IReadOnlyCollection<ClosedIdea> outcomes,
        CancellationToken cancellationToken);

    /// <summary>
    ///     The same, and in the same transaction writes what the autos did in the minute (<paramref name="autos" />) once it has locked the lease of the
    ///     stream of its owner first: without the lease nothing is written and <c>ScannerLeaseLostException</c> goes up. What the autos did with an idea storage
    ///     refused is not written, and the outcome of an idea is written on its paper trades too.
    /// </summary>
    Task<IReadOnlyList<Idea>> SaveAsync(
        IReadOnlyCollection<Idea> created,
        IReadOnlyCollection<Idea> changed,
        IReadOnlyCollection<ClosedIdea> outcomes,
        AutoMinute autos,
        CancellationToken cancellationToken);

    /// <summary>The ideas that closed and still stand, with a trade made of them that has no outcome yet.</summary>
    Task<IReadOnlyList<Idea>> GetClosedOfTradesWithoutOutcomeAsync(CancellationToken cancellationToken);

    /// <summary>The ideas awaiting entry or active.</summary>
    Task<IReadOnlyList<Idea>> GetOpenAsync(CancellationToken cancellationToken);

    /// <summary>The open ideas and the ones closed since <paramref name="closedSince" />, of one pair or of all.</summary>
    Task<IReadOnlyList<Idea>> GetRecentAsync(DateTimeOffset closedSince, Instrument? instrument, CancellationToken cancellationToken);

    Task<Idea?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The ideas of the ids given that are still stored; the ones no longer stored are left out.</summary>
    Task<IReadOnlyList<Idea>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>
    ///     Deletes the ideas that reached the target, the stop or expired before <paramref name="closedBefore" />; returns how many went. The
    ///     open ideas stay however old they are.
    /// </summary>
    Task<int> DeleteClosedBeforeAsync(DateTimeOffset closedBefore, CancellationToken cancellationToken);
}