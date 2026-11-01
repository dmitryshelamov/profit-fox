using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ProfitFox.Application.Backtests.Commands.RunBacktest;
using ProfitFox.Infrastructure.Messaging;

namespace ProfitFox.Infrastructure.BackgroundServices;

/// <summary>Runs queued backtests one at a time, in the order they were queued; one backtest uses several threads itself.</summary>
internal sealed class BacktestBackgroundService(BacktestQueue queue, IServiceScopeFactory scopes, ILogger<BacktestBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (Guid backtestId in queue.ReadAllAsync(stoppingToken))
        {
            // An escaping exception would stop the whole host; the backtest stays as last saved and resumes on the next start.
            try
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RunBacktestCommand(backtestId), stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Backtest {BacktestId} failed outside its own error handling.", backtestId);
            }
        }
    }
}