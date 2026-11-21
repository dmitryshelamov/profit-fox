namespace ProfitFox.Presentation.Contracts.Strategies;

/// <summary>
///     A strategy in the list (spec strategy-constructor «Список стратегий»): its name, description and latest version, or
///     why it does not read.
/// </summary>
public sealed record StrategyListItem(string Name, string? Description, int Version, string? Error);
