using ProfitFox.Application.Signals;
using ProfitFox.Domain.Signals;

namespace ProfitFox.Tests.Unit.Bench.Signals;

/// <summary>
///     The saved signals in memory, to test the rules of the library without a database. It keeps no versions: the versions
///     are the job of the database store.
/// </summary>
public sealed class MemorySignalStore : ISignalStore
{
    private readonly Dictionary<string, SignalDocument> _signals = [];

    public Task<IReadOnlyList<string>> Names(CancellationToken cancellation)
    {
        return Task.FromResult<IReadOnlyList<string>>([.. _signals.Keys]);
    }

    public Task<bool> Exists(string name, CancellationToken cancellation)
    {
        return Task.FromResult(_signals.ContainsKey(name));
    }

    public Task<SignalRead?> Read(string name, CancellationToken cancellation)
    {
        return Task.FromResult(_signals.TryGetValue(name, out SignalDocument? signal) ? new SignalRead(signal, []) : null);
    }

    public Task<SignalDocument> Create(SignalDocument signal, CancellationToken cancellation)
    {
        _signals.Add(signal.Name, signal);
        return Task.FromResult(signal);
    }

    public Task<SignalDocument> Replace(SignalDocument signal, CancellationToken cancellation)
    {
        _signals[signal.Name] = signal;
        return Task.FromResult(signal);
    }

    public Task Rename(string name, string to, CancellationToken cancellation)
    {
        _signals.Add(to, _signals[name] with { Name = to });
        _signals.Remove(name);
        return Task.CompletedTask;
    }

    public Task Delete(string name, CancellationToken cancellation)
    {
        _signals.Remove(name);
        return Task.CompletedTask;
    }
}
