namespace ProfitFox.Domain.Exceptions;

/// <summary>
///     A Worker wanted to end a backtest whose lease it no longer holds: another Worker has taken it over, or it has ended.
///     Nothing is written.
/// </summary>
public sealed class LeaseLostException(string runId, string worker)
    : Exception($"Worker {worker} no longer holds the lease of run {runId}.");
