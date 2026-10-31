using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ProfitFox.Application.MarketData.Commands.RunCandleImport;
using ProfitFox.Infrastructure.Messaging;

namespace ProfitFox.Infrastructure.BackgroundServices;

/// <summary>Runs queued import jobs one at a time, which keeps Bybit load far below its rate limit.</summary>
internal sealed class CandleImportBackgroundService(
    CandleImportQueue queue,
    IServiceScopeFactory scopes,
    ILogger<CandleImportBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (Guid jobId in queue.ReadAllAsync(stoppingToken))
        {
            // An escaping exception would stop the whole host; the job stays as last saved and resumes on the next start.
            // A shutdown cancels the job on purpose, so that is no failure to log.
            try
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RunCandleImportCommand(jobId), stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Candle import job {JobId} failed outside its own error handling.", jobId);
            }
        }
    }
}