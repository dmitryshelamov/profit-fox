using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;

namespace ProfitFox.Domain.Repositories;

/// <summary>
///     Where the saved strategies are kept, by name, with their versions and the versions of signals each of them points at.
///     The repository only keeps them: names and graphs are checked by the strategy commands of the application before they
///     get here.
/// </summary>
public interface IStrategyRepository
{
    /// <summary>
    ///     The names of the saved strategies, in no particular order.
    /// </summary>
    Task<IReadOnlyList<string>> NamesAsync(CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    ///     The latest version of the saved strategy, or the errors that stop it from reading; null when there is none.
    /// </summary>
    Task<StrategyRead?> ReadAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    ///     Keeps a strategy that is not saved yet as its version 1, with the versions of signals its graph points at.
    /// </summary>
    /// <exception cref="StrategyLibraryException">
    ///     The name is taken, also when it was taken a moment ago; or a signal the graph points at was deleted a moment ago.
    /// </exception>
    Task<StrategyDocument> CreateAsync(StrategyDocument strategy, CancellationToken cancellationToken);

    /// <summary>
    ///     Keeps a saved strategy: a graph that differs from the latest version by content makes the next version, otherwise
    ///     only the layout and the description change. Gives the strategy as kept. The kind is written with it: the kind of a
    ///     saved strategy does not change.
    /// </summary>
    /// <exception cref="StrategyLibraryException">
    ///     There is no such strategy, its kind is another, or a signal its graph points at was deleted a moment ago.
    /// </exception>
    Task<StrategyDocument> ReplaceAsync(StrategyDocument strategy, CancellationToken cancellationToken);

    /// <summary>
    ///     Gives the strategy another name and keeps its versions. A strategy saved before it had a kind gets
    ///     <paramref name="kind" /> written; a written kind stays.
    /// </summary>
    /// <exception cref="StrategyLibraryException">There is no such strategy, or the new name is taken.</exception>
    Task RenameAsync(string name, string to, StrategyKind kind, CancellationToken cancellationToken);

    /// <summary>
    ///     Removes the strategy with its versions.
    /// </summary>
    Task DeleteAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    ///     The names of the strategies any version of which points at the signal <paramref name="signal" />, in name order.
    /// </summary>
    Task<IReadOnlyList<string>> UsingSignalAsync(Guid signal, CancellationToken cancellationToken);

    /// <summary>
    ///     The same for every signal by one read: by the id of a signal, the names of the strategies any version of which
    ///     points at it, in name order. A signal no strategy points at is not in the answer.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> UsingSignalsAsync(CancellationToken cancellationToken);
}
