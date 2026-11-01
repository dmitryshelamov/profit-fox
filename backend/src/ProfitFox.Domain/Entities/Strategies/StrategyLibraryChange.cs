using ProfitFox.Domain.Entities.Bots;

namespace ProfitFox.Domain.Entities.Strategies;

/// <summary>What operations on a loaded library changed: the repository writes it at once, with the revision bump.</summary>
public sealed record StrategyLibraryChange(
    IReadOnlyList<Strategy> SavedStrategies,
    IReadOnlyList<Guid> RemovedStrategies,
    IReadOnlyList<CompositeNode> SavedComposites,
    IReadOnlyList<Guid> RemovedComposites,
    IReadOnlyList<StrategyVersion> NewVersions,
    IReadOnlyList<Bot> SavedBots,
    IReadOnlyList<Guid> RemovedBots);