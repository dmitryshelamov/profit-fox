namespace ProfitFox.Application.Ports.Runs;

/// <summary>
///     The run of the queue a Worker computes and the Worker that holds its lease. The line of the run is written only while
///     that Worker holds the lease.
/// </summary>
public sealed record RunAssignment(string RunId, string Worker);
