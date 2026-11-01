using System.Threading.Channels;

using MediatR;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.Backtests.Commands.RunBacktest;

public sealed class RunBacktestCommandHandler(
    IBacktestRepository backtests,
    IStrategyLibraryRepository library,
    ICandleRepository candles,
    ICandleImportJobRepository jobs,
    ICandleImportQueue imports,
    BacktestSettings settings,
    TimeProvider time) : IRequestHandler<RunBacktestCommand>
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(1);

    /// <summary>Takes the backtest through its statuses from where it is: loading candles, then the combinations without a result.</summary>
    public async Task Handle(RunBacktestCommand request, CancellationToken cancellationToken)
    {
        Backtest backtest = (await backtests.GetAsync(request.BacktestId, cancellationToken))!;

        if (backtest.Status == BacktestStatus.Pending)
        {
            Backtest loading = backtest.StartLoading(time.GetUtcNow());
            await backtests.UpdateAsync(loading, BacktestStatus.Pending, cancellationToken);
            backtest = loading;
        }

        if (backtest.Status == BacktestStatus.LoadingCandles)
        {
            backtest = await LoadCandlesAsync(backtest, cancellationToken);
        }

        if (backtest.Status == BacktestStatus.Running)
        {
            await RunAsync(backtest, cancellationToken);
        }
    }

    /// <summary>
    ///     Missing candles are loaded by an import job in the import queue, so that after a restart the job runs once and the backtest
    ///     waits for the same job; the backtest polls the job and counts the stored candles meanwhile. Candles still missing after a
    ///     successful import are not on the exchange: the backtest runs on what is stored and reports the gaps.
    /// </summary>
    private async Task<Backtest> LoadCandlesAsync(Backtest backtest, CancellationToken cancellationToken)
    {
        long expected = backtest.Interval.CandleCount(backtest.From, backtest.To);
        IReadOnlyList<DateTimeOffset> stored = await StoredAsync(backtest, cancellationToken);

        if (backtest.ImportJobId is null && backtest.Interval.MissingRanges(stored, backtest.From, backtest.To).Count > 0)
        {
            var job = new CandleImportJob(
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
            await jobs.AddAsync(job, cancellationToken);
            await imports.EnqueueAsync(job.Id, cancellationToken);
            backtest = backtest.WaitForImport(job.Id);
        }

        while (backtest.ImportJobId is { } jobId)
        {
            CandleImportJob job = (await jobs.GetAsync(jobId, cancellationToken))!;
            stored = await StoredAsync(backtest, cancellationToken);
            backtest = backtest.Loaded(stored.Count, expected);

            if (job.Status == CandleImportStatus.Failed)
            {
                Backtest failed = backtest.Fail(job.Error!, time.GetUtcNow());
                await backtests.UpdateAsync(failed, BacktestStatus.LoadingCandles, cancellationToken);

                return failed;
            }

            if (job.Status == CandleImportStatus.Succeeded)
            {
                break;
            }

            await backtests.UpdateAsync(backtest, BacktestStatus.LoadingCandles, cancellationToken);
            await Task.Delay(PollInterval, time, cancellationToken);
        }

        IReadOnlyList<CandleGap> gaps = [.. backtest.Interval.MissingRanges(stored, backtest.From, backtest.To).Select(gap => new CandleGap(gap.From, gap.To))];
        Backtest running = backtest.Loaded(stored.Count, expected).StartRunning(stored.Count > 0 ? stored[0] : null, gaps);
        await backtests.UpdateAsync(running, BacktestStatus.LoadingCandles, cancellationToken);

        return running;
    }

    private Task<IReadOnlyList<DateTimeOffset>> StoredAsync(Backtest backtest, CancellationToken cancellationToken)
    {
        return candles.GetOpenTimesAsync(backtest.Instrument, backtest.Interval, backtest.From, backtest.To, cancellationToken);
    }

    /// <summary>
    ///     Combinations without a result are shared between threads over candles read once. Results are saved in batches with the
    ///     progress, at least once a second while they come; the speed counts the candles of this run a second.
    /// </summary>
    private async Task RunAsync(Backtest backtest, CancellationToken cancellationToken)
    {
        StrategyVersion version = (await library.GetVersionAsync(backtest.StrategyId, backtest.StrategyVersion, cancellationToken))!;
        IReadOnlyList<Candle> period = await candles.GetAsync(backtest.Instrument, backtest.Interval, backtest.From, backtest.To, int.MaxValue, cancellationToken);
        HashSet<long> calculated = [.. await backtests.GetCalculatedCombinationsAsync(backtest.Id, cancellationToken)];
        ParameterGrid grid = backtest.Grid();
        var results = Channel.CreateUnbounded<BacktestResult>();
        IEnumerable<long> pending = Enumerable.Range(0, (int)backtest.TotalCombinations).Select(combination => (long)combination).Where(combination => !calculated.Contains(combination));
        var options = new ParallelOptions { MaxDegreeOfParallelism = settings.Threads, CancellationToken = cancellationToken };

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
                        backtest.Replay(version.Snapshot, values).Run(backtest.From, period, metrics);
                        await results.Writer.WriteAsync(new BacktestResult(backtest.Id, combination, values, metrics.Metrics(), time.GetElapsedTime(started)), token);
                    });
                }
                finally
                {
                    results.Writer.Complete();
                }
            },
            cancellationToken);

        DateTimeOffset runStarted = time.GetUtcNow();
        DateTimeOffset lastSave = runStarted;
        long calculatedInRun = 0;
        var batch = new List<BacktestResult>();

        await foreach (BacktestResult result in results.Reader.ReadAllAsync(cancellationToken))
        {
            batch.Add(result);

            if (time.GetUtcNow() - lastSave >= SaveInterval)
            {
                await SaveAsync();
            }
        }

        await computing;

        if (batch.Count > 0)
        {
            await SaveAsync();
        }

        await backtests.UpdateAsync(backtest.Succeed(time.GetUtcNow()), BacktestStatus.Running, cancellationToken);

        async Task SaveAsync()
        {
            calculatedInRun += batch.Count;
            DateTimeOffset now = time.GetUtcNow();
            decimal seconds = (decimal)(now - runStarted).TotalSeconds;
            backtest = backtest.Progress(calculated.Count + calculatedInRun, seconds > 0m ? calculatedInRun * period.Count / seconds : null);
            await backtests.SaveProgressAsync(backtest, [.. batch], CancellationToken.None);
            batch.Clear();
            lastSave = now;
        }
    }
}