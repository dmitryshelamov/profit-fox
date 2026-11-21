namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies.Contracts;

/// <summary>
///     A row of <c>strategy_signals</c>: a signal node of a version of a strategy and the version of the signal it points at.
/// </summary>
internal sealed record StrategySignalDb(Guid StrategyId, int Version, string Node, Guid SignalId, int SignalVersion);
