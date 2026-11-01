using System.Threading.Channels;

using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.Backtests.Commands.RunNextBacktestWork;

public sealed class RunNextBacktestWorkCommandHandler(
    IBacktestRepository backtests,
    IStrategyLibraryRepository library,
    ICandleRepository candles,
    ICandleImportJobRepository jobs,
    BacktestInputCache inputs,
    BacktestSettings backtestSettings,
    WorkerSettings settings,
    WorkerIdentity worker,
    EngineVersion engine,
    TimeProvider time) : IRequestHandler<RunNextBacktestWorkCommand, bool>
{
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Goes through the unfinished backtests of this Worker's engine version from the oldest and stops at the first chunk it can lease, so that one Worker finishes a
    ///     backtest before it starts the next one, while a Worker that finds every chunk of a backtest taken goes on to the next. A backtest
    ///     still loading its candles takes one step on the way. Every write expects the status the step saw, so Workers that take the same
    ///     step at once end the same way, and a cancel or the deletion of the bot stops the work.
    /// </summary>
    public async Task<bool> Handle(RunNextBacktestWorkCommand request, CancellationToken cancellationToken)
    {
        // No Worker of an older version will take these up again, and one of a newer version will not compute them by its rules.
        await backtests.FailOutdatedAsync(engine.Number, Backtest.EngineVersionChanged, time.GetUtcNow(), cancellationToken);

        foreach (Backtest unfinished in await backtests.GetUnfinishedAsync(engine.Number, cancellationToken))
        {
            Backtest? backtest = unfinished.Status == BacktestStatus.Running ? unfinished : await PrepareAsync(unfinished, cancellationToken);

            if (backtest is null)
            {
                continue;
            }

            DateTimeOffset now = time.GetUtcNow();
            BacktestChunk? chunk = await backtests.ClaimChunkAsync(backtest.Id, worker.Id, now, now + settings.LeaseDuration, cancellationToken);

            if (chunk is null)
            {
                continue;
            }

            if (chunk.LostLeases == BacktestChunk.MaxLostLeases)
            {
                await backtests.FailAsync(backtest.Id, [BacktestStatus.Running], Backtest.InterruptedChunk, now, cancellationToken);
            }
            else
            {
                await RunChunkAsync(backtest, chunk, cancellationToken);
            }

            return true;
        }

        return false;
    }

    /// <summary>
    ///     One step of a backtest before it runs. Missing candles are loaded by an import job, which a Worker runs like any other; the
    ///     backtest counts the stored candles while it waits. Candles still missing after a successful import are not on the exchange: the
    ///     backtest runs on what is stored and reports the gaps.
    /// </summary>
    /// <returns>
    ///     The backtest running, or <c>null</c> while it still loads or once it failed. A write that finds the backtest moved on writes
    ///     nothing, so a step after a cancel, or after another Worker took the same step, ends with no chunk to claim or with its chunks.
    /// </returns>
    private async Task<Backtest?> PrepareAsync(Backtest backtest, CancellationToken cancellationToken)
    {
        if (backtest.Status == BacktestStatus.Pending)
        {
            backtest = backtest.StartLoading(time.GetUtcNow());
            await backtests.UpdateAsync(backtest, BacktestStatus.Pending, cancellationToken);
        }

        // The job is read before the candles: the candles of a job that has just succeeded are all counted.
        CandleImportJob? job = backtest.ImportJobId is { } jobId ? await jobs.GetAsync(jobId, cancellationToken) : null;
        long expected = backtest.Interval.CandleCount(backtest.From, backtest.To);
        IReadOnlyList<DateTimeOffset> stored = await candles.GetOpenTimesAsync(backtest.Instrument, backtest.Interval, backtest.From, backtest.To, cancellationToken);
        IReadOnlyList<(DateTimeOffset From, DateTimeOffset To)> missing = backtest.Interval.MissingRanges(stored, backtest.From, backtest.To);
        backtest = backtest.Loaded(stored.Count, expected);

        if (job is null && missing.Count > 0)
        {
            var import = new CandleImportJob(
                Guid.NewGuid(),
                backtest.Instrument,
                backtest.Interval,
                backtest.From,
                backtest.To,
                CandleImportStatus.Pending,
                ImportedCandles: 0,
                Error: null,
                time.GetUtcNow(),
                StartedAt: null,
                FinishedAt: null);
            await backtests.StartImportAsync(backtest.WaitForImport(import.Id), import, cancellationToken);

            return null;
        }

        if (job?.Status == CandleImportStatus.Failed)
        {
            await backtests.FailAsync(backtest.Id, [BacktestStatus.LoadingCandles], job.Error!, time.GetUtcNow(), cancellationToken);

            return null;
        }

        if (job is not null && job.Status != CandleImportStatus.Succeeded)
        {
            await backtests.UpdateAsync(backtest, BacktestStatus.LoadingCandles, cancellationToken);

            return null;
        }

        IReadOnlyList<CandleGap> gaps = [.. missing.Select(gap => new CandleGap(gap.From, gap.To))];
        Backtest running = backtest.StartRunning(stored.Count > 0 ? stored[0] : null, gaps);
        IReadOnlyList<BacktestChunk> chunks = BacktestChunk.Split(running.Id, running.TotalCombinations, settings.ChunkSize);

        await backtests.StartRunningAsync(running, chunks, cancellationToken);

        return running;
    }

    /// <summary>Computes the chunk while this Worker holds its lease; a lost lease or a host shutdown stops it without another write.</summary>
    private async Task RunChunkAsync(Backtest backtest, BacktestChunk chunk, CancellationToken cancellationToken)
    {
        using var work = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var done = new CancellationTokenSource();
        Task keeping = LeaseKeeper.KeepAsync((until, token) => backtests.RenewChunkLeaseAsync(chunk, worker.Id, until, token), settings, time, work, done.Token);

        try
        {
            await ComputeAsync(backtest, chunk, work);
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // The lease is lost: the Worker that took it over goes on with the chunk.
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new BacktestWorkException(backtest.Id, exception);
        }
        finally
        {
            await done.CancelAsync();
            await keeping;
        }
    }

    /// <summary>
    ///     Combinations of the chunk without a result are shared between threads over candles read once per backtest. Results are saved in
    ///     batches with the speed, at least once a second while they come; the speed counts the candles of this run a second. The last
    ///     chunk done ends the backtest.
    /// </summary>
    private async Task ComputeAsync(Backtest backtest, BacktestChunk chunk, CancellationTokenSource work)
    {
        BacktestInputs input = await inputs.GetAsync(backtest.Id, () => LoadAsync(backtest, work.Token));
        HashSet<long> calculated = [.. await backtests.GetCalculatedCombinationsAsync(backtest.Id, work.Token)];
        ParameterGrid grid = backtest.Grid();
        var results = Channel.CreateUnbounded<BacktestResult>();
        IEnumerable<long> pending = Enumerable.Range(0, chunk.Combinations).Select(offset => chunk.First + offset).Where(combination => !calculated.Contains(combination));
        var options = new ParallelOptions { MaxDegreeOfParallelism = backtestSettings.Threads, CancellationToken = work.Token };

        var computing = Task.Run(
            async () =>
            {
                try
                {
                    await Parallel.ForEachAsync(pending, options, async (combination, token) =>
                    {
                        long started = time.GetTimestamp();
                        IReadOnlyDictionary<string, NodeValue> values = grid.Combination(combination);
                        var metrics = new MetricsObserver(backtest.Simulation.Balance);
                        backtest.Replay(input.Snapshot, values).Run(backtest.From, input.Candles, metrics);
                        await results.Writer.WriteAsync(new BacktestResult(backtest.Id, combination, values, metrics.Metrics(), time.GetElapsedTime(started)), token);
                    });
                }
                finally
                {
                    results.Writer.Complete();
                }
            },
            work.Token);

        DateTimeOffset runStarted = time.GetUtcNow();
        DateTimeOffset lastSave = runStarted;
        long calculatedInRun = 0;
        var batch = new List<BacktestResult>();

        await foreach (BacktestResult result in results.Reader.ReadAllAsync(work.Token))
        {
            batch.Add(result);

            if (time.GetUtcNow() - lastSave >= SaveInterval && !await SaveAsync())
            {
                await work.CancelAsync();
                await computing.ContinueWith(_ => { }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

                return;
            }
        }

        try
        {
            await computing;
        }
        catch (Exception exception) when (!work.IsCancellationRequested)
        {
            // The results saved so far stay; the backtest ends instead of failing again in every Worker that takes the chunk over.
            await backtests.FailAsync(backtest.Id, [BacktestStatus.Running], $"A combination failed: {exception.Message}", time.GetUtcNow(), CancellationToken.None);

            return;
        }

        if (batch.Count > 0)
        {
            await SaveAsync();
        }

        // A chunk of a lost lease is not completed, and a backtest that stopped running does not succeed.
        await backtests.CompleteChunkAsync(chunk, worker.Id, CancellationToken.None);
        await backtests.SucceedIfCompleteAsync(backtest.Id, time.GetUtcNow(), CancellationToken.None);

        async Task<bool> SaveAsync()
        {
            calculatedInRun += batch.Count;
            DateTimeOffset now = time.GetUtcNow();
            decimal seconds = (decimal)(now - runStarted).TotalSeconds;
            decimal? speed = seconds > 0m ? calculatedInRun * input.Candles.Count / seconds : null;
            bool saved = await backtests.SaveChunkProgressAsync(chunk, worker.Id, [.. batch], speed, now, CancellationToken.None);
            batch.Clear();
            lastSave = now;

            return saved;
        }
    }

    private async Task<BacktestInputs> LoadAsync(Backtest backtest, CancellationToken cancellationToken)
    {
        StrategyVersion version = (await library.GetVersionAsync(backtest.StrategyId, backtest.StrategyVersion, cancellationToken))!;
        IReadOnlyList<Candle> period = await candles.GetAsync(backtest.Instrument, backtest.Interval, backtest.From, backtest.To, int.MaxValue, cancellationToken);

        return new BacktestInputs(backtest.Id, version.Snapshot, period);
    }
}