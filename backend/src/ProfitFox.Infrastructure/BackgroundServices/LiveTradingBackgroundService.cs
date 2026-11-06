using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ProfitFox.Application.IdeaTrades.Commands.ClaimIdeaTrade;
using ProfitFox.Application.IdeaTrades.Commands.RunIdeaTrade;
using ProfitFox.Application.IdeaTrades.Runtime;
using ProfitFox.Application.LiveTrading.Commands.ClaimLiveBot;
using ProfitFox.Application.LiveTrading.Commands.RunLiveBot;
using ProfitFox.Application.LiveTrading.Runtime;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.LiveTrading;

namespace ProfitFox.Infrastructure.BackgroundServices;

/// <summary>
///     The live trading lane of a trading Worker: takes every bot and every trade by an idea it can and keeps each in a session of its
///     own scope, so one Worker keeps many, and runs the one loop for the bots and the one for the trades that renew their leases and
///     read their changes. A failed session leaves
///     its lease to expire; any Worker takes the bot again then. A Worker that stops gives up the bots it kept.
/// </summary>
internal sealed class LiveTradingBackgroundService(
    IServiceScopeFactory scopes,
    LiveBotLeases leases,
    IdeaTradeLeases tradeLeases,
    WorkerSettings settings,
    WorkerIdentity worker,
    TimeProvider time,
    ILogger<LiveTradingBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Task leasing = leases.RunAsync(stoppingToken);
        Task tradeLeasing = tradeLeases.RunAsync(stoppingToken);
        var sessions = new List<(Task Session, LeasedBot Leased)>();
        var tradeSessions = new List<(Task Session, LeasedIdeaTrade Leased)>();

        while (!stoppingToken.IsCancellationRequested)
        {
            LeasedBot? leased = null;

            try
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                leased = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new ClaimLiveBotCommand(), stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Taking a live bot failed.");
            }

            if (leased is not null)
            {
                sessions.Add((RunAsync(leased, stoppingToken), leased));

                continue;
            }

            LeasedIdeaTrade? trade = null;

            try
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                trade = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new ClaimIdeaTradeCommand(), stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Taking a trade by an idea failed.");
            }

            if (trade is not null)
            {
                tradeSessions.Add((RunAsync(trade, stoppingToken), trade));

                continue;
            }

            // A session ended by a stop that came while a bot was taken stays: its bot is given up with the others. A session ends
            // only after the stop that ends it, so the stop is seen once the session is.
            sessions.RemoveAll(item => item.Session.IsCompleted && !stoppingToken.IsCancellationRequested);
            tradeSessions.RemoveAll(item => item.Session.IsCompleted && !stoppingToken.IsCancellationRequested);
            await Task.Delay(settings.PollInterval, time, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
        }

        await Task.WhenAll(sessions.Select(item => item.Session).Concat(tradeSessions.Select(item => item.Session)));
        await leasing;
        await tradeLeasing;

        // The sessions are over and write nothing more: the next Worker takes their bots at once, not when the leases end.
        await using AsyncServiceScope release = scopes.CreateAsyncScope();
        await release.ServiceProvider.GetRequiredService<ILiveBotRepository>()
            .ReleaseAllAsync(worker.Id, [.. sessions.Select(item => (item.Leased.Bot.BotId, item.Leased.Epoch))], CancellationToken.None);
        await release.ServiceProvider.GetRequiredService<IIdeaTradeRepository>()
            .ReleaseAllAsync(worker.Id, [.. tradeSessions.Select(item => (item.Leased.Trade.Id, item.Leased.Epoch))], CancellationToken.None);
    }

    private async Task RunAsync(LeasedIdeaTrade leased, CancellationToken stoppingToken)
    {
        await Task.Yield();

        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RunIdeaTradeCommand(leased), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The Worker stops.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Trade by an idea {TradeId} failed.", leased.Trade.Id);
        }
    }

    private async Task RunAsync(LeasedBot leased, CancellationToken stoppingToken)
    {
        await Task.Yield();

        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RunLiveBotCommand(leased), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The Worker stops.
        }
        catch (Exception exception)
        {
            // Also while the Worker stops: the SDK answers a call cut by the stop with a failure, and the leases are given up after.
            logger.LogError(exception, "Live bot {BotId} failed.", leased.Bot.BotId);
        }
    }
}