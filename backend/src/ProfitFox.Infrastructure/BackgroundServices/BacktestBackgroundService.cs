using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ProfitFox.Application.Backtests.Commands.RunNextBacktestWork;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Workers;

namespace ProfitFox.Infrastructure.BackgroundServices;

/// <summary>The backtest lane of a Worker: one chunk at a time, which itself uses several threads.</summary>
internal sealed class BacktestBackgroundService(
    IServiceScopeFactory scopes,
    WorkerSettings settings,
    TimeProvider time,
    ILogger<BacktestBackgroundService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return WorkerLoop.RunAsync(
            new RunNextBacktestWorkCommand(),
            exception =>
            {
                if (exception is BacktestWorkException work)
                {
                    logger.LogError(work.InnerException, "Backtest {BacktestId} failed outside its own error handling.", work.BacktestId);
                }
                else
                {
                    logger.LogError(exception, "Backtest work failed outside its own error handling.");
                }
            },
            scopes,
            settings.PollInterval,
            time,
            stoppingToken);
    }
}