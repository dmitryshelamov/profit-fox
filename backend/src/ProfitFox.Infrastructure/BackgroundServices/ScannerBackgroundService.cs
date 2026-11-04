using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitFox.Application.Ports.Market;
using ProfitFox.Application.Scanner;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Infrastructure.BackgroundServices;

/// <summary>
///     The one lane of the scanner (decision 2): it waits for the lease of the stream, and while it holds it runs a session in a scope of
///     its own, renewing the lease every <see cref="ScannerSettings.LeaseRenewal" />. A renewal that does not go through ends the session:
///     the stream is taken off and nothing more is written. A failed session is logged, and the scanner waits for the lease again.
/// </summary>
internal sealed class ScannerBackgroundService(
    IServiceScopeFactory scopes,
    IScannerStateRepository state,
    ScannerSettings settings,
    WorkerIdentity identity,
    IScannerJournal journal,
    TimeProvider time) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lease = new WorkerSettings { LeaseDuration = settings.LeaseDuration, LeaseRenewal = settings.LeaseRenewal };

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                DateTimeOffset now = time.GetUtcNow();

                if (await state.ClaimAsync(identity.Id, now, now + settings.LeaseDuration, stoppingToken))
                {
                    await HoldAsync(lease, stoppingToken);
                }

                await Task.Delay(settings.LeaseRenewal, time, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // The scanner stops; its lease expires and another scanner takes the stream.
            }
            catch (Exception exception)
            {
                journal.Failed("lease", exception);
                await Task.Delay(settings.LeaseRenewal, time, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }

    private async Task HoldAsync(WorkerSettings lease, CancellationToken stoppingToken)
    {
        journal.Took();
        using var work = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var done = new CancellationTokenSource();
        Task keeper = LeaseKeeper.KeepAsync((until, token) => state.RenewAsync(identity.Id, until, token), lease, time, work, done.Token);

        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ScannerSession>().RunAsync(work.Token);
        }
        catch (Exception exception) when (exception is ScannerLeaseLostException || (exception is OperationCanceledException && work.IsCancellationRequested && !stoppingToken.IsCancellationRequested))
        {
            journal.Lost();
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            journal.Failed("session", exception);
        }
        finally
        {
            await done.CancelAsync();
            await keeper;
        }
    }
}