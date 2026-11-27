namespace ProfitFox.Domain.ValueObjects.Runs;

/// <summary>
///     The backtest of the queue a Worker computes and the Worker that holds its lease. Its end is written only while that
///     Worker holds the lease.
/// </summary>
public sealed record RunAssignment(string RunId, string Worker);
