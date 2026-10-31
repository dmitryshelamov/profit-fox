namespace ProfitFox.Application.Ports.MarketData;

/// <summary>Signals the background worker that a job is waiting; the job's state lives in the store.</summary>
public interface ICandleImportQueue
{
    ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken);
}