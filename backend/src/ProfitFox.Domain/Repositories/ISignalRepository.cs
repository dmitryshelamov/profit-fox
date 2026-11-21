using ProfitFox.Domain.Entities.Signals;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Signals;

namespace ProfitFox.Domain.Repositories;

/// <summary>
///     Where the saved signals are kept, by name, with their versions. The repository only keeps them: names and graphs are
///     checked by the signal commands of the application before they get here.
/// </summary>
public interface ISignalRepository
{
    /// <summary>
    ///     The names of the saved signals, in no particular order.
    /// </summary>
    Task<IReadOnlyList<string>> NamesAsync(CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    ///     The latest version of the saved signal, or the errors that stop it from reading; null when there is none.
    /// </summary>
    Task<SignalRead?> ReadAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    ///     The versions the references point at, by one read: one for each reference whose signal is saved, with the current
    ///     name and the latest version of the signal; its graph is null when there is no such version or its text does not
    ///     read. A reference to a signal that is not saved gives nothing.
    /// </summary>
    Task<IReadOnlyList<SignalVersion>> ReadVersionsAsync(IReadOnlyCollection<SignalReference> references, CancellationToken cancellationToken);

    /// <summary>
    ///     Keeps a signal that is not saved yet as its version 1.
    /// </summary>
    /// <exception cref="SignalLibraryException">The name is taken, also when it was taken a moment ago.</exception>
    Task<SignalDocument> CreateAsync(SignalDocument signal, CancellationToken cancellationToken);

    /// <summary>
    ///     Keeps a saved signal: a graph that differs from the latest version by content makes the next version, otherwise only
    ///     the layout and the description change. Gives the signal as kept.
    /// </summary>
    /// <exception cref="SignalLibraryException">There is no such signal.</exception>
    Task<SignalDocument> ReplaceAsync(SignalDocument signal, CancellationToken cancellationToken);

    /// <summary>
    ///     Gives the signal another name and keeps its versions.
    /// </summary>
    /// <exception cref="SignalLibraryException">There is no such signal, or the new name is taken.</exception>
    Task RenameAsync(string name, string to, CancellationToken cancellationToken);

    /// <summary>
    ///     Removes the signal with its versions.
    /// </summary>
    /// <exception cref="SignalLibraryException">
    ///     A version of a saved strategy points at the signal, also one saved a moment ago: <see cref="SignalProblem.InUse" />
    ///     with the names of the strategies.
    /// </exception>
    Task DeleteAsync(string name, CancellationToken cancellationToken);
}
