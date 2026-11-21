namespace ProfitFox.Presentation.Contracts.Strategies;

/// <summary>
///     A signal of a strategy in the list (spec strategy-constructor «Сигналы и выходы стратегии в списке»): its name, the
///     version the nodes of the strategy point at and the latest version of the signal.
/// </summary>
public sealed record StrategySignalItem(string Name, int Version, int LatestVersion);
