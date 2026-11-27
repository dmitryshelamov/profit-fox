using MediatR;

using Microsoft.Extensions.Logging;

using ProfitFox.Application.Commands.Backtests.ExecuteBacktest;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Runs;

namespace ProfitFox.Application.Commands.Runs.RunNext;

/// <summary>
///     What a Worker does with the queue (spec bench-run-queue): takes the backtest that was queued first, keeps its lease
///     while it computes it, reports its progress and stops it when its owner cancels it. One at a time. A backtest ends
///     done, failed or cancelled in every case but two, which write nothing: the Worker is shut down, or it lost the lease;
///     another Worker then takes it over. A backtest is computed by <see cref="ExecuteBacktestCommand" />.
/// </summary>
internal sealed class RunNextCommandHandler(
    IBacktestRepository backtests,
    ISender sender,
    TimeProvider time,
    ILogger<RunNextCommandHandler> logger) : IRequestHandler<RunNextCommand, RunNextResult>
{
    public async Task<RunNextResult> Handle(RunNextCommand request, CancellationToken cancellationToken)
    {
        WorkerSettings settings = request.Settings;
        Backtest? backtest = await backtests.ClaimAsync(settings.Name, settings.LeaseDuration, cancellationToken);
        if (backtest is null)
        {
            return RunNextResult.NothingToTake;
        }

        await Take(backtest, settings, cancellationToken);
        return RunNextResult.Ran;
    }

    private async Task Take(Backtest backtest, WorkerSettings settings, CancellationToken shutdown)
    {
        try
        {
            await Compute(backtest, settings, shutdown);
        }
        catch (LeaseLostException exception)
        {
            Log($"Backtest {backtest.Id}: {exception.Message} Nothing is written.");
        }
        catch (Exception exception) when (!shutdown.IsCancellationRequested)
        {
            // A backtest that failed is ended already; a failure here left it going, and it comes back after its lease.
            Log($"Backtest {backtest.Id} failed: {exception.Message}");
        }
        catch (Exception) when (shutdown.IsCancellationRequested)
        {
            Log($"Backtest {backtest.Id} stopped: the Worker is shut down. Its lease is left to expire.");
        }
    }

    private async Task Compute(Backtest backtest, WorkerSettings settings, CancellationToken shutdown)
    {
        var assignment = new RunAssignment(backtest.Id, settings.Name);
        if (backtest.InterruptedTooOften)
        {
            await backtests.StopAsync(assignment, BacktestState.Failed, $"прерван {Backtest.MaxLostLeases} раза", shutdown);
            return;
        }

        if (backtest.CancelRequested)
        {
            // The Worker that went the backtest died after the owner asked to stop it.
            await backtests.StopAsync(assignment, BacktestState.Cancelled, "отменён до начала расчёта", shutdown);
            return;
        }

        var progress = new RunProgress();
        bool cancelRequested = false;
        using var work = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        using var done = new CancellationTokenSource();
        Task keeper = LeaseKeeper.KeepAsync(
            async renewing =>
            {
                Renewal? renewal = await backtests.RenewAsync(backtest.Id, settings.Name, settings.LeaseDuration, progress.Done, progress.Total, renewing);
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
            await sender.Send(new ExecuteBacktestCommand(backtest, assignment, progress), work.Token);
        }
        catch (Exception) when (work.IsCancellationRequested && !shutdown.IsCancellationRequested)
        {
            // The backtest stopped because its owner asked, or because the lease is lost. Ending one with a lost lease is refused.
            if (Volatile.Read(ref cancelRequested))
            {
                await backtests.StopAsync(assignment, BacktestState.Cancelled, $"отменён: пройдено {progress.Done} пар из {progress.Total}", CancellationToken.None);
            }
        }
        finally
        {
            await done.CancelAsync();
            await keeper;
        }
    }

    private void Log(string line)
    {
        logger.LogInformation("{Line}", line);
    }
}
