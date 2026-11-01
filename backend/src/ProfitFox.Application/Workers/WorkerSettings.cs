namespace ProfitFox.Application.Workers;

/// <summary>The <c>Worker</c> section of the Worker's settings.</summary>
public sealed class WorkerSettings
{
    /// <summary>How long a Worker with nothing to do waits before it asks the database again.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>How long work stays with the Worker that took it unless the Worker renews the lease.</summary>
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How often a Worker renews the lease of the work it runs; shorter than <see cref="LeaseDuration" />.</summary>
    public TimeSpan LeaseRenewal { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Combinations of a backtest in one chunk.</summary>
    public int ChunkSize { get; init; } = 100;
}