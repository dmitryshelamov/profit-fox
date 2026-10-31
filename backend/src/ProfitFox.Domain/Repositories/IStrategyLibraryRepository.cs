using ProfitFox.Domain.Entities.Strategies;

namespace ProfitFox.Domain.Repositories;

/// <summary>Stores the strategy library; a save or delete at a revision that is no longer current changes nothing and returns <c>false</c>.</summary>
public interface IStrategyLibraryRepository
{
    Task<StrategyLibrary> LoadAsync(CancellationToken cancellationToken);

    Task<bool> SaveStrategyAsync(Strategy strategy, long expectedRevision, CancellationToken cancellationToken);

    Task<bool> DeleteStrategyAsync(Guid id, long expectedRevision, CancellationToken cancellationToken);

    Task<bool> SaveCompositeAsync(CompositeNode composite, long expectedRevision, CancellationToken cancellationToken);

    Task<bool> DeleteCompositeAsync(Guid id, long expectedRevision, CancellationToken cancellationToken);

    Task<Strategy?> GetStrategyAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Strategy>> GetStrategiesAsync(CancellationToken cancellationToken);

    Task<CompositeNode?> GetCompositeAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<CompositeNode>> GetCompositesAsync(CancellationToken cancellationToken);
}