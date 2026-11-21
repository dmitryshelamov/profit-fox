using ProfitFox.Domain.Entities.Base;
using ProfitFox.Domain.ValueObjects.Runs;

namespace ProfitFox.Domain.Entities.Runs;

/// <summary>
///     A run of the queue as its row says: what it is, where it is, who holds it and how far it has come. A run is born and
///     changes only through the statements of its repository (design add-bench-service, decision 3): the time of the
///     database decides when it was queued, who takes it and when its lease ends, so Workers on different machines agree. The
///     entity only reads what its row says.
/// </summary>
public sealed class Run : AggregateRoot
{
    /// <summary>
    ///     The run that loses its lease this many times ends as failed, so a run that kills its Worker does not go on forever.
    /// </summary>
    public const int MaxLostLeases = 3;

    public string Id { get; }

    public string Hypothesis { get; }

    public int Version { get; }

    public string Period { get; }

    public bool Parity { get; }

    /// <summary>
    ///     One of <see cref="RunState" />.
    /// </summary>
    public string Status { get; }

    public DateTimeOffset QueuedAt { get; }

    public DateTimeOffset? StartedAt { get; }

    public DateTimeOffset? FinishedAt { get; }

    public string? LeaseOwner { get; }

    public DateTimeOffset? LeasedUntil { get; }

    public int LostLeases { get; }

    public bool CancelRequested { get; }

    public int PairsDone { get; }

    public int? PairsTotal { get; }

    /// <summary>
    ///     The run has lost its lease too often to be computed again.
    /// </summary>
    public bool InterruptedTooOften => LostLeases >= MaxLostLeases;

    /// <summary>
    ///     The run waits in the queue and no Worker has taken it.
    /// </summary>
    public bool IsQueued => Status == RunState.Queued;

    private Run(
        string id,
        string hypothesis,
        int version,
        string period,
        bool parity,
        string status,
        DateTimeOffset queuedAt,
        DateTimeOffset? startedAt,
        DateTimeOffset? finishedAt,
        string? leaseOwner,
        DateTimeOffset? leasedUntil,
        int lostLeases,
        bool cancelRequested,
        int pairsDone,
        int? pairsTotal)
    {
        Id = id;
        Hypothesis = hypothesis;
        Version = version;
        Period = period;
        Parity = parity;
        Status = status;
        QueuedAt = queuedAt;
        StartedAt = startedAt;
        FinishedAt = finishedAt;
        LeaseOwner = leaseOwner;
        LeasedUntil = leasedUntil;
        LostLeases = lostLeases;
        CancelRequested = cancelRequested;
        PairsDone = pairsDone;
        PairsTotal = pairsTotal;
    }

    /// <summary>
    ///     A stored run brought back as its row says, for a repository: nothing happens to it, so it changes no state and raises no
    ///     event.
    /// </summary>
    public static Run Restore(
        string id,
        string hypothesis,
        int version,
        string period,
        bool parity,
        string status,
        DateTimeOffset queuedAt,
        DateTimeOffset? startedAt,
        DateTimeOffset? finishedAt,
        string? leaseOwner,
        DateTimeOffset? leasedUntil,
        int lostLeases,
        bool cancelRequested,
        int pairsDone,
        int? pairsTotal)
    {
        return new Run(
            id,
            hypothesis,
            version,
            period,
            parity,
            status,
            queuedAt,
            startedAt,
            finishedAt,
            leaseOwner,
            leasedUntil,
            lostLeases,
            cancelRequested,
            pairsDone,
            pairsTotal);
    }

    /// <summary>
    ///     The run is of the version <paramref name="version" /> of the hypothesis <paramref name="hypothesis" /> on the period
    ///     <paramref name="period" /> in the kind <paramref name="parity" />.
    /// </summary>
    public bool IsOf(string hypothesis, int version, string period, bool parity)
    {
        return Hypothesis == hypothesis && Version == version && Period == period && Parity == parity;
    }
}
