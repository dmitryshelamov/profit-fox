using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;

namespace ProfitFox.Tests.Unit.Application.Commands.Strategies;

/// <summary>
///     The saved strategies in memory, to test the rules of the library without a database. It numbers no versions — the
///     versions are the job of the database repository — but remembers every graph each strategy was saved with, so it knows
///     which signals any version points at.
/// </summary>
public sealed class MemoryStrategyRepository : IStrategyRepository
{
    private readonly Dictionary<string, StrategyDocument> _strategies = [];
    private readonly Dictionary<Guid, List<GraphDefinition>> _graphs = [];
    private readonly Dictionary<string, string> _broken = [];

    /// <summary>
    ///     How many times the strategies of all the signals were read.
    /// </summary>
    public int UsageReads { get; private set; }

    /// <summary>
    ///     Makes the saved strategy <paramref name="name" /> one that does not read, with <paramref name="error" />.
    /// </summary>
    public void Break(string name, string error)
    {
        _broken[name] = error;
    }

    public Task<IReadOnlyList<string>> NamesAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<string>>([.. _strategies.Keys]);
    }

    public Task<bool> ExistsAsync(string name, CancellationToken cancellationToken)
    {
        return Task.FromResult(_strategies.ContainsKey(name));
    }

    public Task<StrategyRead?> ReadAsync(string name, CancellationToken cancellationToken)
    {
        if (_broken.TryGetValue(name, out string? error))
        {
            return Task.FromResult<StrategyRead?>(new StrategyRead(Strategy: null, [error]));
        }

        return Task.FromResult(_strategies.TryGetValue(name, out StrategyDocument? strategy) ? new StrategyRead(strategy, []) : null);
    }

    // The graphs are kept in the order they were saved, so version n is the n-th graph.
    public Task<StrategyRead?> ReadVersionAsync(string name, int version, CancellationToken cancellationToken)
    {
        if (!_strategies.TryGetValue(name, out StrategyDocument? strategy) || version < 1 || version > _graphs[strategy.Id].Count)
        {
            return Task.FromResult<StrategyRead?>(null);
        }

        GraphDefinition graph = _graphs[strategy.Id][version - 1];
        var read = StrategyDocument.Restore(strategy.Id, strategy.Name, strategy.Description, graph, strategy.Layout, version, kind: strategy.Kind);
        return Task.FromResult<StrategyRead?>(new StrategyRead(read, []));
    }

    public Task<StrategyDocument> CreateAsync(StrategyDocument strategy, CancellationToken cancellationToken)
    {
        _strategies.Add(strategy.Name, strategy);
        _graphs[strategy.Id] = [strategy.Graph];
        return Task.FromResult(strategy);
    }

    public Task<StrategyDocument> ReplaceAsync(StrategyDocument strategy, CancellationToken cancellationToken)
    {
        StrategyDocument current = _strategies[strategy.Name];
        var kept = StrategyDocument.Restore(current.Id, strategy.Name, strategy.Description, strategy.Graph, strategy.Layout, current.Version, kind: strategy.Kind);
        _strategies[strategy.Name] = kept;
        _graphs[current.Id].Add(strategy.Graph);
        return Task.FromResult(kept);
    }

    public Task RenameAsync(string name, string to, StrategyKind kind, CancellationToken cancellationToken)
    {
        StrategyDocument strategy = _strategies[name];
        strategy.Rename(to);
        _strategies.Add(to, strategy);
        _strategies.Remove(name);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken)
    {
        if (_strategies.Remove(name, out StrategyDocument? strategy))
        {
            _graphs.Remove(strategy.Id);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> UsingSignalAsync(Guid signal, CancellationToken cancellationToken)
    {
        string[] names =
        [
            .. _strategies.Values
                .Where(strategy => _graphs[strategy.Id].Any(graph => SignalNodes.References(graph).Any(reference => reference.Signal == signal)))
                .Select(strategy => strategy.Name)
                .Order(StringComparer.Ordinal)
        ];
        return Task.FromResult<IReadOnlyList<string>>(names);
    }

    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> UsingSignalsAsync(CancellationToken cancellationToken)
    {
        UsageReads++;
        var used = _strategies.Values
            .SelectMany(strategy => _graphs[strategy.Id].SelectMany(SignalNodes.References).Select(reference => (reference.Signal, strategy.Name)))
            .Distinct()
            .GroupBy(pair => pair.Signal, pair => pair.Name)
            .ToDictionary(group => group.Key, IReadOnlyList<string> (group) => [.. group.Order(StringComparer.Ordinal)]);
        return Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<string>>>(used);
    }
}
