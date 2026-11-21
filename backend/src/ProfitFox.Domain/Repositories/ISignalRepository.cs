using ProfitFox.Domain.Entities.Signals;
using ProfitFox.Domain.Exceptions;
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
    Task DeleteAsync(string name, CancellationToken cancellationToken);
}
