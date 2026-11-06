using ProfitFox.Domain.Entities.IdeaAutos;

namespace ProfitFox.Domain.Repositories;

/// <summary>The entry requests of the autos with money; the scanner writes them with its ideas, a trading Worker takes them one at a time.</summary>
public interface IIdeaAutoEntryRepository
{
    /// <summary>
    ///     Takes the session lock of the entries, which holds a connection of its own until the handle is disposed, so the requests go one at a time
    ///     over all the Workers; none when another Worker holds it.
    /// </summary>
    Task<IAsyncDisposable?> TryLockEntriesAsync(CancellationToken cancellationToken);

    /// <summary>The earliest request by the minute, the place of the auto, the order of the idea and the id; none when there is no request.</summary>
    Task<IdeaAutoEntry?> GetNextAsync(CancellationToken cancellationToken);

    /// <summary>Locks the row of a request in the unit of work; none when it is gone.</summary>
    Task<IdeaAutoEntry?> LockAsync(Guid id, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    ///     Turns the requests written before <paramref name="olderThan" /> that no Worker took into refusals «Не успел войти» at <paramref name="now" />,
    ///     skipping the rows a Worker holds; returns how many.
    /// </summary>
    Task<int> ExpireAsync(DateTimeOffset olderThan, DateTimeOffset now, CancellationToken cancellationToken);
}