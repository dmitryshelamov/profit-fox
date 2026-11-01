namespace ProfitFox.Application.Exceptions;

/// <summary>Work on an import job failed outside its own error handling; the Worker logs it with the job and goes on.</summary>
public sealed class CandleImportWorkException(Guid jobId, Exception inner)
    : Exception($"Work on import job {jobId} failed: {inner.Message}", inner)
{
    public Guid JobId { get; } = jobId;
}