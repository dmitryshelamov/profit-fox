using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ProfitFox.Application.Workers;

namespace ProfitFox.Infrastructure.BackgroundServices;

/// <summary>Logs which machine this Worker runs on under which lease owner, so a lease in the database leads to the machine.</summary>
internal sealed class WorkerStartLogService(WorkerIdentity worker, ILogger<WorkerStartLogService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Worker {WorkerId} runs on {Machine}.", worker.Id, Environment.MachineName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}