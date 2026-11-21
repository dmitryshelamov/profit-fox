using ProfitFox.Domain.Entities.Signals;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Signals;

namespace ProfitFox.Tests.Unit.Application.Commands.Signals;

/// <summary>
///     The saved signals in memory, to test the rules of the library without a database. It keeps no versions: the versions
///     are the job of the database repository.
/// </summary>
public sealed class MemorySignalRepository : ISignalRepository
{
    private readonly Dictionary<string, SignalDocument> _signals = [];

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
        return Task.FromResult(_signals.TryGetValue(name, out SignalDocument? signal) ? new SignalRead(signal, []) : null);
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
