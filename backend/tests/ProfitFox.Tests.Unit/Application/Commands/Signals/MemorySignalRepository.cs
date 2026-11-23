using ProfitFox.Domain.Entities.Signals;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Signals;

namespace ProfitFox.Tests.Unit.Application.Commands.Signals;

/// <summary>
///     The saved signals in memory, to test the rules of the library without a database. It keeps no versions: the versions
///     are the job of the database repository.
/// </summary>
public sealed class MemorySignalRepository : ISignalRepository
{
    private readonly Dictionary<string, SignalDocument> _signals = [];
    private readonly Dictionary<string, string> _broken = [];

    /// <summary>
    ///     How many times versions of signals were read.
    /// </summary>
    public int VersionReads { get; private set; }

    /// <summary>
    ///     How many times a signal was read by its name (task 18.13).
    /// </summary>
    public int Reads { get; private set; }

    /// <summary>
    ///     The saved signals as they are, without reading them.
    /// </summary>
    public IEnumerable<SignalDocument> Documents => _signals.Values;

    /// <summary>
    ///     Makes the saved signal <paramref name="name" /> one that does not read, with <paramref name="error" />.
    /// </summary>
    public void Break(string name, string error)
    {
        _broken[name] = error;
    }

    public Task<IReadOnlyList<string>> NamesAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<string>>([.. _signals.Keys]);
    }

    public Task<bool> ExistsAsync(string name, CancellationToken cancellationToken)
    {
        return Task.FromResult(_signals.ContainsKey(name));
    }

    public Task<SignalRead?> ReadAsync(string name, CancellationToken cancellationToken)
    {
        Reads++;
        if (_broken.TryGetValue(name, out string? error))
        {
            return Task.FromResult<SignalRead?>(new SignalRead(Signal: null, [error]));
        }

        return Task.FromResult(_signals.TryGetValue(name, out SignalDocument? signal) ? new SignalRead(signal, []) : null);
    }

    /// <summary>
    ///     Only the latest version of each signal is kept, so only it is found.
    /// </summary>
    public Task<IReadOnlyList<SignalVersion>> ReadVersionsAsync(IReadOnlyCollection<SignalReference> references, CancellationToken cancellationToken)
    {
        VersionReads++;
        List<SignalVersion> versions = [];
        foreach (SignalReference reference in references)
        {
            if (_signals.Values.FirstOrDefault(signal => signal.Id == reference.Signal) is { } signal)
            {
                versions.Add(new SignalVersion(reference, signal.Name, signal.Version, reference.Version == signal.Version ? signal.Graph : null));
            }
        }

        return Task.FromResult<IReadOnlyList<SignalVersion>>(versions);
    }

    public Task<SignalDocument> CreateAsync(SignalDocument signal, CancellationToken cancellationToken)
    {
        _signals.Add(signal.Name, signal);
        return Task.FromResult(signal);
    }

    public Task<SignalDocument> ReplaceAsync(SignalDocument signal, CancellationToken cancellationToken)
    {
        _signals[signal.Name] = signal;
        return Task.FromResult(signal);
    }

    public Task RenameAsync(string name, string to, CancellationToken cancellationToken)
    {
        SignalDocument signal = _signals[name];
        signal.Rename(to);
        _signals.Add(to, signal);
        _signals.Remove(name);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken)
    {
        _signals.Remove(name);
        return Task.CompletedTask;
    }
}
