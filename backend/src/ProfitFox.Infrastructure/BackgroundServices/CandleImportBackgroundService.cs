using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.MarketData.Commands.RunNextCandleImport;
using ProfitFox.Application.Workers;

namespace ProfitFox.Infrastructure.BackgroundServices;

/// <summary>The import lane of a Worker: one job at a time of all Workers, which keeps Bybit load far below its rate limit.</summary>
internal sealed class CandleImportBackgroundService(
    IServiceScopeFactory scopes,
    WorkerSettings settings,
    TimeProvider time,
    ILogger<CandleImportBackgroundService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return WorkerLoop.RunAsync(
            new RunNextCandleImportCommand(),
            exception =>
            {
                if (exception is CandleImportWorkException work)
                {
                    logger.LogError(work.InnerException, "Candle import job {JobId} failed outside its own error handling.", work.JobId);
                }
                else
                {
                    logger.LogError(exception, "A candle import job failed outside its own error handling.");
                }
            },
            scopes,
            settings.PollInterval,
            time,
            stoppingToken);
    }
}