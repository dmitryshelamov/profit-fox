using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ProfitFox.Application.IdeaAutos.Commands.RunNextAutoEntry;
using ProfitFox.Application.Workers;

namespace ProfitFox.Infrastructure.BackgroundServices;

/// <summary>
///     The lane of a trading Worker that makes the entries of the autos with money: one request at a time of all Workers, the earliest first, and a wait
///     of <c>Worker:PollInterval</c> when there is none. A failure of one request is logged and the lane goes on.
/// </summary>
internal sealed class IdeaAutoEntryBackgroundService(
    IServiceScopeFactory scopes,
    WorkerSettings settings,
    TimeProvider time,
    ILogger<IdeaAutoEntryBackgroundService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return WorkerLoop.RunAsync(
            new RunNextAutoEntryCommand(),
            exception => logger.LogError(exception, "An entry request of an auto failed."),
            scopes,
            settings.PollInterval,
            time,
            stoppingToken);
    }
}