using MediatR;

using Microsoft.Extensions.Logging;

using ProfitFox.Application.Commands.Runs.ExecuteRun;
using ProfitFox.Application.Commands.Runs.StopRun;
using ProfitFox.Domain.Entities.Runs;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.ValueObjects.Ledger;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Domain.ValueObjects.Runs.Enums;

namespace ProfitFox.Application.Commands.Runs.RunNext;

/// <summary>
///     What a Worker does with the queue (spec bench-run-queue): takes the run that was queued first, keeps its lease while it
///     computes it, reports its progress and stops it when its owner cancels it. One run at a time. A run ends in a line of the
///     ledger in every case but two, which write nothing: the Worker is shut down, or it lost the lease; another Worker then
///     takes the run over. The run is computed by <see cref="ExecuteRunCommand" /> and ended without computing by
///     <see cref="StopRunCommand" />.
/// </summary>
internal sealed class RunNextCommandHandler(
    IRunRepository runs,
    ISender sender,
    TimeProvider time,
    ILogger<RunNextCommandHandler> logger) : IRequestHandler<RunNextCommand, RunNextResult>
{
    public async Task<RunNextResult> Handle(RunNextCommand request, CancellationToken cancellationToken)
    {
        WorkerSettings settings = request.Settings;
        Run? run = await runs.ClaimAsync(settings.Name, settings.LeaseDuration, cancellationToken);
        if (run is null)
        {
            return RunNextResult.NothingToTake;
        }

        try
        {
            await Compute(run, settings, cancellationToken);
        }
        catch (LeaseLostException exception)
        {
            Log($"Run {run.Id}: {exception.Message} Nothing is written.");
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A run that failed has its line already; a failure here left the run without one, and it comes back after its lease.
            Log($"Run {run.Id} failed: {exception.Message}");
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            Log($"Run {run.Id} stopped: the Worker is shut down. Its lease is left to expire.");
        }

        return RunNextResult.Ran;
    }

    private async Task Compute(Run run, WorkerSettings settings, CancellationToken shutdown)
    {
        Periods.TryParse(run.Period, out Period period);
        DateTimeOffset now = time.GetUtcNow();
        var today = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
        var assignment = new RunAssignment(run.Id, settings.Name);
        if (run.InterruptedTooOften)
        {
            await sender.Send(Stop(run, period, today, assignment, RunStatus.Failed, $"прерван {Run.MaxLostLeases} раза"), shutdown);
            return;
        }

        if (run.CancelRequested)
        {
            // The Worker that went the run died after the owner asked to stop it.
            await sender.Send(Stop(run, period, today, assignment, RunStatus.Cancelled, "отменён до начала расчёта"), shutdown);
            return;
        }

        var progress = new RunProgress();
        bool cancelRequested = false;
        using var work = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        using var done = new CancellationTokenSource();
        Task keeper = LeaseKeeper.KeepAsync(
            async renewing =>
            {
                Renewal? renewal = await runs.RenewAsync(run.Id, settings.Name, settings.LeaseDuration, progress.Done, progress.Total, renewing);
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
            await sender.Send(new ExecuteRunCommand(run.Hypothesis, run.Version, period, run.Parity, today, assignment, progress), work.Token);
        }
        catch (Exception) when (work.IsCancellationRequested && !shutdown.IsCancellationRequested)
        {
            // The run stopped because its owner asked, or because the lease is lost. Writing the line of a lost lease is refused.
            if (Volatile.Read(ref cancelRequested))
            {
                await sender.Send(
                    Stop(run, period, today, assignment, RunStatus.Cancelled, $"отменён: пройдено {progress.Done} пар из {progress.Total}"),
                    CancellationToken.None);
            }
        }
        finally
        {
            await done.CancelAsync();
            await keeper;
        }
    }

    private static StopRunCommand Stop(Run run, Period period, DateTimeOffset today, RunAssignment assignment, string status, string reason)
    {
        return new StopRunCommand(run.Hypothesis, run.Version, period, run.Parity, today, assignment, status, reason);
    }

    private void Log(string line)
    {
        logger.LogInformation("{Line}", line);
    }
}
