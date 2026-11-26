using System.Diagnostics;

using MediatR;

using Microsoft.Extensions.Logging;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Queries.MarketData;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.MarketData;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Runs;

namespace ProfitFox.Application.Commands.Backtests.ExecuteBacktest;

/// <summary>
///     A backtest of the queue (change add-strategy-backtest, design.md, decision 4; change fix-backtest-random-control,
///     design.md, decisions 4 and 6): pair by pair in the order of their symbols, every set on the history of the pair, in two
///     passes for a category of ideas (<see cref="BacktestPasses" />), never after the end of history and with every first
///     read added to the hash of the backtest. A pair counts as half passed after each pass of a category of ideas: the
///     progress is half the sum of the pairs of both passes, rounded down. A read the market-data service dropped is repeated after the pauses of
///     <see cref="HistoryRepeats" /> (task 12.12); the time of reading counts the pauses too. Only the orders between the passes, and the numbers of the sets and of the pairs and what is kept of the orders with a
///     trade to the end, stay in memory. The backtest ends as done with
///     them in one step, or as failed with the error when the calculation fails; a cancelled backtest or one whose Worker
///     lost the lease writes nothing here. The line of the journal on its end tells what the cache of history did for it
///     (change add-backtest-history-cache, decision 9): the Worker computes one backtest at a time and the scanner does not
///     read through the cache, so the difference of the counters is that of the backtest.
/// </summary>
internal sealed class ExecuteBacktestCommandHandler(
    IHistorySource source,
    IInstrumentCatalog instruments,
    IBacktestRepository backtests,
    HistoryRepeats repeats,
    IHistoryCacheCounters cache,
    TimeProvider time,
    ILogger<ExecuteBacktestCommandHandler> logger) : IRequestHandler<ExecuteBacktestCommand, ExecuteBacktestResult>
{
    /// <exception cref="OperationCanceledException">The backtest was cancelled or the Worker is shut down; nothing is written.</exception>
    /// <exception cref="LeaseLostException">The Worker lost the lease; nothing is written.</exception>
    public async Task<ExecuteBacktestResult> Handle(ExecuteBacktestCommand request, CancellationToken cancellationToken)
    {
        try
        {
            return await Run(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or LeaseLostException) && !cancellationToken.IsCancellationRequested)
        {
            string reason = exception is HistoryUnavailableException ? $"история недоступна: {exception.Message}" : exception.Message;
            await backtests.StopAsync(request.Assignment, BacktestState.Failed, reason, CancellationToken.None);
            throw;
        }
    }

    private async Task<ExecuteBacktestResult> Run(ExecuteBacktestCommand request, CancellationToken cancellationToken)
    {
        Backtest backtest = request.Backtest;
        BacktestSettings settings = backtest.Settings;
        var clock = Stopwatch.StartNew();
        HistoryCacheCounts before = cache.Counts;
        var timed = new TimedHistorySource(new RepeatingHistorySource(source, repeats, time, logger));
        using var hash = new HistoryHash();
        var history = new MarketHistory(timed, settings.End, hash, time);
        IReadOnlyList<BacktestPlan> plans = BacktestPlans.Of(backtest.Graph, backtest.Kind, settings, [.. backtest.Sets.Select(set => set.Set)]);
        IReadOnlyList<Instrument> listed = await instruments.LinearUsdtPerpetuals(cancellationToken);
        Instrument[] pairs = [.. Pairs(listed, settings.Pairs)];
        IReadOnlyDictionary<TimeSpan, IReadOnlyDictionary<string, IReadOnlyList<HistoryCandle>>>? named = await PairBacktestRun.NamedAsync(history, StrategyGraph.NamedPairs(backtest.Graph), listed, plans, cancellationToken);
        request.Progress?.Begin(pairs.Length);
        var halves = new PairHalves(request.Progress);
        IReadOnlyList<SetTotals> totals = await PairBacktestRun.RunAsync(history, pairs, backtest.Kind, plans, cancellationToken, named, halves.Passed);

        TimeSpan read = timed.Elapsed;
        TimeSpan compute = clock.Elapsed - read;
        var outcome = new BacktestOutcome(
            [
                .. plans.Select((plan, index) => (
                    plan.Set,
                    SetSummary.Of(totals[index].Numbers),
                    (IReadOnlyList<PairSummary>)[.. PairNumbers.Ordered(totals[index].Pairs).Select(pair => new PairSummary(pair.Pair, SetSummary.Of(pair.Numbers)))]))
            ],
            EngineVersion.Number,
            hash.Result(),
            read.TotalSeconds,
            compute > TimeSpan.Zero ? compute.TotalSeconds : 0,
            pairs.Length);
        await backtests.FinishAsync(request.Assignment, outcome, cancellationToken);
        HistoryCacheCounts cached = cache.Counts - before;
        string ofCache = cached == HistoryCacheCounts.Zero
            ? ""
            : $", cache: {cached.FromCache} months from the cache, {cached.Saved} saved, {cached.Requests} requests to the service";
        logger.LogInformation("{Line}", $"Backtest {backtest.Id}: {pairs.Length} pairs, {plans.Count} sets, read {read.TotalSeconds:F1} s, computed {compute.TotalSeconds:F1} s{ofCache}");
        return new ExecuteBacktestResult(outcome);
    }

    // The pairs of the settings that are listed, or every listed pair, in the order of their symbols.
    private static IEnumerable<Instrument> Pairs(IReadOnlyList<Instrument> listed, IReadOnlyList<string>? chosen)
    {
        IEnumerable<Instrument> pairs = chosen is null ? listed : listed.Where(pair => chosen.Contains(pair.Symbol, StringComparer.Ordinal));
        return pairs.OrderBy(pair => pair.Symbol, StringComparer.Ordinal);
    }
}