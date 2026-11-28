using Npgsql;

using ProfitFox.Application.Commands.Runs.RunNext;
using ProfitFox.Application.Commands.Trades.RunTrader;
using ProfitFox.Application.Services.Trades;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Infrastructure.Persistence.Postgres;
using ProfitFox.Worker.Logging;

namespace ProfitFox.Worker.BackgroundServices;

/// <summary>
///     The lane of the Worker for the trades by ideas (change take-idea-trades, design.md, decision 2), on when
///     <c>Trading:Enabled</c>: one Worker of the bench holds the lease of the trader and sends the orders and leads the trades;
///     the others try to take it every <see cref="WorkerSettings.LeaseRenewal" /> and take the trades over once the holder
///     stopped. The lease is renewed and kept as that of the scanner; a lost lease cancels the trader, which closes its streams.
/// </summary>
internal sealed class TradeBackgroundService(
    NpgsqlDataSource database,
    IServiceScopeFactory scopes,
    ITraderLeaseRepository lease,
    WorkerSettings worker,
    TradingSettings settings,
    TimeProvider time,
    ILogger<TradeBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Enabled)
        {
            return;
        }

        try
        {
            await BenchSchema.WaitFor(database, worker.SchemaPollInterval, new LogWriter(logger), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                bool taken = false;
                try
                {
                    taken = await lease.TakeAsync(worker.Name, worker.LeaseDuration, stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(exception, "The Worker could not ask for the lease of the trader.");
                }

                if (!taken || !await Trade(stoppingToken))
                {
                    await Task.Delay(worker.LeaseRenewal, time, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The Worker is shut down.
        }
        finally
        {
            try
            {
                await lease.ReleaseAsync(worker.Name, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "The Worker could not give the lease of the trader up.");
            }
        }
    }

    // The trader while the lease holds; false when it failed or lost the lease.
    private async Task<bool> Trade(CancellationToken stoppingToken)
    {
        using var work = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var done = new CancellationTokenSource();
        Task keeper = LeaseKeeper.KeepAsync(token => lease.TakeAsync(worker.Name, worker.LeaseDuration, token), worker, time, work, done.Token);
        try
        {
            await scopes.SendAsync(new RunTraderCommand(worker.Name), work.Token);
            return true;
        }
        catch (LeaseLostException exception)
        {
            logger.LogWarning(exception, "The trader lost its lease: another Worker leads the trades.");
            return false;
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            if (exception is OperationCanceledException)
            {
                logger.LogWarning("The trader lost its lease and stopped.");
                return false;
            }

            logger.LogError(exception, "The trader stopped.");
            return false;
        }
        finally
        {
            await done.CancelAsync();
            await keeper;
        }
    }
}
