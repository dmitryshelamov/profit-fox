using ProfitFox.Domain.Entities.Strategies;

namespace ProfitFox.Domain.ValueObjects.Strategies;

/// <summary>
///     A saved strategy as the store read it: the strategy, or every error that stops it.
/// </summary>
public sealed record StrategyRead(StrategyDocument? Strategy, IReadOnlyList<string> Errors);
