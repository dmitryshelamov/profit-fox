namespace ProfitFox.Application.Contracts.Strategies;

/// <summary>
///     A signal of a strategy in the list of the library (spec strategy-constructor «Сигналы и выходы стратегии в списке»):
///     its current name, the version the nodes of the strategy point at and the latest version of the signal.
/// </summary>
public sealed record StrategySignalInternal(string Name, int Version, int LatestVersion);
