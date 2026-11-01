using MediatR;

using Microsoft.Extensions.DependencyInjection;

namespace ProfitFox.Infrastructure.BackgroundServices;

/// <summary>Asks for the next work right after a piece of work and waits <c>Worker:PollInterval</c> when there was none.</summary>
internal static class WorkerLoop
{
    /// <param name="next">The command that takes the next work and returns whether it found any.</param>
    /// <param name="failed">Logs a failure of one piece of work; the loop goes on.</param>
    /// <param name="scopes">Every piece of work runs in its own scope.</param>
    /// <param name="pollInterval">The wait when there was no work.</param>
    /// <param name="time">The clock of the wait.</param>
    /// <param name="stoppingToken">The host stops.</param>
    public static async Task RunAsync(
        IRequest<bool> next,
        Action<Exception> failed,
        IServiceScopeFactory scopes,
        TimeSpan pollInterval,
        TimeProvider time,
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var found = false;

            // An escaping exception would stop the whole host; the work stays as last saved, and its lease expires for any Worker.
            // A shutdown cancels the work on purpose, so that is no failure to log.
            try
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                found = await scope.ServiceProvider.GetRequiredService<ISender>().Send(next, stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                failed(exception);
            }

            if (!found)
            {
                await Task.Delay(pollInterval, time, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }
}