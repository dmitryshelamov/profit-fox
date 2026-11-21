namespace ProfitFox.Application.Commands.Runs;

/// <summary>
///     The <c>Worker</c> section of the settings of the Worker (spec bench-run-queue «Аренда прогона»).
/// </summary>
public sealed class WorkerSettings
{
    /// <summary>
    ///     The name the run shows while this Worker goes it: the machine and the process.
    /// </summary>
    public string Name { get; init; } = $"{Environment.MachineName}-{Environment.ProcessId}";

    /// <summary>
    ///     How long a Worker with nothing to do waits before it asks the queue again.
    /// </summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     How long a run stays with the Worker that took it unless the Worker renews the lease.
    /// </summary>
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     How often a Worker renews the lease of its run and reports its progress; shorter than <see cref="LeaseDuration" />.
    ///     A cancelled run stops no later than this plus the time of the pair it is on.
    /// </summary>
    public TimeSpan LeaseRenewal { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     How often a Worker looks whether the database has its schema yet.
    /// </summary>
    public TimeSpan SchemaPollInterval { get; init; } = TimeSpan.FromSeconds(5);
}
