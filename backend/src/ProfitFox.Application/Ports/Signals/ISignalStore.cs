using ProfitFox.Application.Commands.Signals;
using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.ValueObjects.Signals;

namespace ProfitFox.Application.Ports.Signals;

/// <summary>
///     Where the saved signals are kept, by name, with their versions. The store only keeps them: names and graphs are
///     checked by <see cref="SignalLibrary" /> before they get here.
/// </summary>
public interface ISignalStore
{
    /// <summary>
    ///     The names of the saved signals, in no particular order.
    /// </summary>
    Task<IReadOnlyList<string>> Names(CancellationToken cancellation);

    Task<bool> Exists(string name, CancellationToken cancellation);

    /// <summary>
    ///     The latest version of the saved signal, or the errors that stop it from reading; null when there is none.
    /// </summary>
    Task<SignalRead?> Read(string name, CancellationToken cancellation);

    /// <summary>
    ///     Keeps a signal that is not saved yet as its version 1.
    /// </summary>
    /// <exception cref="SignalLibraryException">The name is taken, also when it was taken a moment ago.</exception>
    Task<SignalDocument> Create(SignalDocument signal, CancellationToken cancellation);

    /// <summary>
    ///     Keeps a saved signal: a graph that differs from the latest version by content makes the next version, otherwise only
    ///     the layout and the description change. Gives the signal as kept.
    /// </summary>
    /// <exception cref="SignalLibraryException">There is no such signal.</exception>
    Task<SignalDocument> Replace(SignalDocument signal, CancellationToken cancellation);

    /// <summary>
    ///     Gives the signal another name and keeps its versions.
    /// </summary>
    /// <exception cref="SignalLibraryException">There is no such signal, or the new name is taken.</exception>
    Task Rename(string name, string to, CancellationToken cancellation);

    /// <summary>
    ///     Removes the signal with its versions.
    /// </summary>
    Task Delete(string name, CancellationToken cancellation);
}
