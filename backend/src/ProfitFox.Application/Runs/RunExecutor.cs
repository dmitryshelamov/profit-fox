using ProfitFox.Domain.Ledger;
using ProfitFox.Domain.Runs;

namespace ProfitFox.Application.Runs;

/// <summary>
///     What a Worker does with the queue (spec bench-run-queue): takes the run that was queued first, keeps its lease while it
///     computes it, reports its progress and stops it when its owner cancels it. One run at a time. A run ends in a line of the
///     ledger in every case but two, which write nothing: the Worker is shut down, or it lost the lease; another Worker then
///     takes the run over.
/// </summary>
public sealed class RunExecutor(IRunQueue queue, HypothesisRunner runner, WorkerSettings settings, TimeProvider time, TextWriter log)
{
    /// <summary>
    ///     The run that loses its lease this many times ends as failed, so a run that kills its Worker does not go on forever.
    /// </summary>
    public const int MaxLostLeases = 3;

    /// <summary>
    ///     Takes the next run of the queue and goes it to the end.
    /// </summary>
    /// <returns>False when the queue had nothing to take.</returns>
    public async Task<bool> RunNext(CancellationToken shutdown)
    {
        RunRecord? run = await queue.Claim(settings.Name, settings.LeaseDuration, shutdown);
        if (run is null)
        {
            return false;
        }

        try
        {
            await Run(run, shutdown);
        }
        catch (LeaseLostException exception)
        {
            log.WriteLine($"Run {run.Id}: {exception.Message} Nothing is written.");
        }
        catch (Exception exception) when (!shutdown.IsCancellationRequested)
        {
            // A run that failed has its line already; a failure here left the run without one, and it comes back after its lease.
            log.WriteLine($"Run {run.Id} failed: {exception.Message}");
        }
        catch (Exception) when (shutdown.IsCancellationRequested)
        {
            log.WriteLine($"Run {run.Id} stopped: the Worker is shut down. Its lease is left to expire.");
        }

        return true;
    }

    private async Task Run(RunRecord run, CancellationToken shutdown)
    {
        Periods.TryParse(run.Period, out Period period);
        DateTimeOffset now = time.GetUtcNow();
        var options = new RunOptions(
            run.Hypothesis,
            run.Version,
            period,
            run.Parity,
            new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero),
            new RunAssignment(run.Id, settings.Name));
        if (run.LostLeases >= MaxLostLeases)
        {
            await runner.Stop(options, RunStatus.Failed, $"прерван {MaxLostLeases} раза", shutdown);
            return;
        }

        if (run.CancelRequested)
        {
            // The Worker that went the run died after the owner asked to stop it.
            await runner.Stop(options, RunStatus.Cancelled, "отменён до начала расчёта", shutdown);
            return;
        }

        var progress = new RunProgress();
        bool cancelRequested = false;
        using var work = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        using var done = new CancellationTokenSource();
        Task keeper = LeaseKeeper.KeepAsync(
            async renewing =>
            {
                Renewal? renewal = await queue.Renew(run.Id, settings.Name, settings.LeaseDuration, progress.Done, progress.Total, renewing);
                if (renewal is { CancelRequested: true })
                {
                    cancelRequested = true;
                    await work.CancelAsync();
                }

                return renewal is not null;
            },
            settings,
            time,
            work,
            done.Token);
        try
        {
            await runner.Run(options, work.Token, progress);
        }
        catch (Exception) when (work.IsCancellationRequested && !shutdown.IsCancellationRequested)
        {
            // The run stopped because its owner asked, or because the lease is lost. Writing the line of a lost lease is refused.
            if (Volatile.Read(ref cancelRequested))
            {
                await runner.Stop(options, RunStatus.Cancelled, $"отменён: пройдено {progress.Done} пар из {progress.Total}", CancellationToken.None);
            }
        }
        finally
        {
            await done.CancelAsync();
            await keeper;
        }
    }
}
