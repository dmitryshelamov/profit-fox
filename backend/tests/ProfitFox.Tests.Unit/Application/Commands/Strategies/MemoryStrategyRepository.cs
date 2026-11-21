using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Strategies;

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
        return Task.FromResult(_strategies.TryGetValue(name, out StrategyDocument? strategy) ? new StrategyRead(strategy, []) : null);
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
        var kept = StrategyDocument.Restore(current.Id, strategy.Name, strategy.Description, strategy.Graph, strategy.Layout, current.Version);
        _strategies[strategy.Name] = kept;
        _graphs[current.Id].Add(strategy.Graph);
        return Task.FromResult(kept);
    }

    public Task RenameAsync(string name, string to, CancellationToken cancellationToken)
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
}
