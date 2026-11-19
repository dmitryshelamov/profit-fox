using ProfitFox.Bench.Domain.Signals;

namespace ProfitFox.Bench.Application.Signals;

/// <summary>
///     Where the saved signals are kept, by name. The store only keeps them: names and graphs are checked by
///     <see cref="SignalLibrary" /> before they get here.
/// </summary>
public interface ISignalStore
{
    /// <summary>
    ///     The names of the saved signals, in no particular order.
    /// </summary>
    IReadOnlyList<string> Names();

    bool Exists(string name);

    /// <summary>
    ///     The saved signal, or the errors that stop it from reading; null when there is none.
    /// </summary>
    SignalRead? Read(string name);

    /// <summary>
    ///     Writes the signal whole or not at all.
    /// </summary>
    void Write(SignalDocument signal);

    void Delete(string name);
}
