using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.Strategies;

namespace ProfitFox.Domain.Repositories;

/// <summary>Stores the strategy library; a save at a revision that is no longer current changes nothing and returns <c>false</c>.</summary>
public interface IStrategyLibraryRepository
{
    /// <summary>The library with the latest version of each strategy and the bots.</summary>
    Task<StrategyLibrary> LoadAsync(CancellationToken cancellationToken);

    /// <summary>Writes the whole change and bumps the revision in one transaction.</summary>
    Task<bool> SaveAsync(StrategyLibraryChange change, long expectedRevision, CancellationToken cancellationToken);

    Task<Strategy?> GetStrategyAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Strategy>> GetStrategiesAsync(CancellationToken cancellationToken);

    Task<CompositeNode?> GetCompositeAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<CompositeNode>> GetCompositesAsync(CancellationToken cancellationToken);

    Task<Bot?> GetBotAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Bot>> GetBotsAsync(CancellationToken cancellationToken);

    Task<int?> GetLatestVersionNumberAsync(Guid strategyId, CancellationToken cancellationToken);

    Task<StrategyVersion?> GetVersionAsync(Guid strategyId, int number, CancellationToken cancellationToken);
}