namespace ProfitFox.Application.Workers;

/// <summary>This Worker process as the owner of the leases it takes; a new process is a new owner.</summary>
public sealed record WorkerIdentity(Guid Id);