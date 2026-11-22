namespace ProfitFox.Presentation.Contracts.Strategies;

/// <summary>
///     The last backtest of a strategy: its id, its state and the version of the strategy it is of.
/// </summary>
public sealed record StrategyLastRunResponse(string Id, string State, int Version);