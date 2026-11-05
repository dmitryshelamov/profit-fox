using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ProfitFox.Application.LiveTrading.Commands.ClaimLiveBot;
using ProfitFox.Application.LiveTrading.Commands.RunLiveBot;
using ProfitFox.Application.LiveTrading.Runtime;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.LiveTrading;

namespace ProfitFox.Infrastructure.BackgroundServices;

/// <summary>
///     The live trading lane of a trading Worker: takes every bot it can and keeps each in a session of its own scope, so one
///     Worker keeps many bots, and runs the one loop that renews their leases and reads their changes. A failed session leaves
///     its lease to expire; any Worker takes the bot again then. A Worker that stops gives up the bots it kept.
/// </summary>
internal sealed class LiveTradingBackgroundService(
    IServiceScopeFactory scopes,
    LiveBotLeases leases,
    WorkerSettings settings,
    WorkerIdentity worker,
    TimeProvider time,
    ILogger<LiveTradingBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Task leasing = leases.RunAsync(stoppingToken);
        var sessions = new List<(Task Session, LeasedBot Leased)>();

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

            // A session ended by a stop that came while a bot was taken stays: its bot is given up with the others. A session ends
            // only after the stop that ends it, so the stop is seen once the session is.
            sessions.RemoveAll(item => item.Session.IsCompleted && !stoppingToken.IsCancellationRequested);
            await Task.Delay(settings.PollInterval, time, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
        }

        await Task.WhenAll(sessions.Select(item => item.Session));
        await leasing;

        // The sessions are over and write nothing more: the next Worker takes their bots at once, not when the leases end.
        await using AsyncServiceScope release = scopes.CreateAsyncScope();
        await release.ServiceProvider.GetRequiredService<ILiveBotRepository>()
            .ReleaseAllAsync(worker.Id, [.. sessions.Select(item => (item.Leased.Bot.BotId, item.Leased.Epoch))], CancellationToken.None);
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