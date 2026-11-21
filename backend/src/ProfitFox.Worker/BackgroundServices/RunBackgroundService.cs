using Npgsql;

using ProfitFox.Application.Commands.Runs.RunNext;
using ProfitFox.Infrastructure.Persistence.Postgres;
using ProfitFox.Worker.Logging;

namespace ProfitFox.Worker.BackgroundServices;

/// <summary>
///     The lane of the Worker for runs: waits for the schema of the database, then takes the next run of the queue and goes it
///     to the end, one at a time, and asks the queue again every <see cref="WorkerSettings.PollInterval" /> when it is empty.
/// </summary>
internal sealed class RunBackgroundService(
    NpgsqlDataSource database,
    IServiceScopeFactory scopes,
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
                RunNextResult result = RunNextResult.NothingToTake;
                try
                {
                    result = await scopes.SendAsync(new RunNextCommand(settings), stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(exception, "The Worker could not take or go a run.");
                }

                if (result == RunNextResult.NothingToTake)
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
