namespace ProfitFox.Application.Exceptions;

/// <summary>
///     A Worker wanted to write the line of a run whose lease it no longer holds: another Worker has taken the run over, or it
///     has ended. Nothing is written.
/// </summary>
public sealed class LeaseLostException(string runId, string worker)
    : Exception($"Worker {worker} no longer holds the lease of run {runId}.");
