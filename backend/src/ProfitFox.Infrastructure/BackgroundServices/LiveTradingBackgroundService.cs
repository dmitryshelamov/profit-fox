using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ProfitFox.Application.LiveTrading.Commands.ClaimLiveBot;
using ProfitFox.Application.LiveTrading.Commands.RunLiveBot;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.ValueObjects.LiveTrading;

namespace ProfitFox.Infrastructure.BackgroundServices;

/// <summary>
///     The live trading lane of a trading Worker: takes every bot it can and keeps each in a session of its own scope, so one
///     Worker keeps many bots. A failed session leaves its lease to expire; any Worker takes the bot again then.
/// </summary>
internal sealed class LiveTradingBackgroundService(
    IServiceScopeFactory scopes,
    WorkerSettings settings,
    TimeProvider time,
    ILogger<LiveTradingBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sessions = new List<Task>();

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
                sessions.Add(RunAsync(leased, stoppingToken));

                continue;
            }

            sessions.RemoveAll(session => session.IsCompleted);
            await Task.Delay(settings.PollInterval, time, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
        }

        await Task.WhenAll(sessions);
    }

    private async Task RunAsync(LeasedBot leased, CancellationToken stoppingToken)
    {
        await Task.Yield();

        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RunLiveBotCommand(leased), stoppingToken);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Live bot {BotId} failed; its lease expires for any Worker.", leased.Bot.BotId);
        }
    }
}