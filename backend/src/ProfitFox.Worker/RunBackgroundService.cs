using Npgsql;

using ProfitFox.Application.Runs;
using ProfitFox.Infrastructure.Persistence;

namespace ProfitFox.Worker;

/// <summary>
///     The lane of the Worker for runs: waits for the schema of the database, then takes the next run of the queue and goes it
///     to the end, one at a time, and asks the queue again every <see cref="WorkerSettings.PollInterval" /> when it is empty.
/// </summary>
internal sealed class RunBackgroundService(
    NpgsqlDataSource database,
    RunExecutor executor,
    WorkerSettings settings,
    TimeProvider time,
    ILogger<RunBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            logger.LogInformation("Worker {Name} starts.", settings.Name);
            await BenchSchema.WaitFor(database, settings.SchemaPollInterval, new LogWriter(logger), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                bool worked = false;
                try
                {
                    worked = await executor.RunNext(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(exception, "The Worker could not take or go a run.");
                }

                if (!worked)
                {
                    await Task.Delay(settings.PollInterval, time, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The Worker is shut down.
        }
    }
}
