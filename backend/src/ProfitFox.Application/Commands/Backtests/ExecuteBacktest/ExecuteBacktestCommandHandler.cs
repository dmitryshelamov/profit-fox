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
using ProfitFox.Domain.Services.MarketData;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Runs;

namespace ProfitFox.Application.Commands.Backtests.ExecuteBacktest;

/// <summary>
///     A backtest of the queue (change add-strategy-backtest, design.md, decision 4): pair by pair in the order of their
///     symbols, every set on the history of the pair read once, never after the end of history and with every read added to
///     the hash of the backtest. A read the market-data service dropped is repeated after the pauses of
///     <see cref="HistoryRepeats" /> (task 12.12); the time of reading counts the pauses too. Only the numbers of the sets and of the pairs stay in memory. The backtest ends as done with
///     them in one step, or as failed with the error when the calculation fails; a cancelled backtest or one whose Worker
///     lost the lease writes nothing here.
/// </summary>
internal sealed class ExecuteBacktestCommandHandler(
    IHistorySource source,
    IInstrumentCatalog instruments,
    IBacktestRepository backtests,
    HistoryRepeats repeats,
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
        var timed = new TimedHistorySource(new RepeatingHistorySource(source, repeats, time, logger));
        using var hash = new HistoryHash();
        var history = new MarketHistory(timed, settings.End, hash, time);
        IReadOnlyList<BacktestPlan> plans = BacktestPlans.Of(backtest.Graph, backtest.Kind, settings, [.. backtest.Sets.Select(set => set.Set)]);
        Instrument[] pairs = [.. Pairs(await instruments.LinearUsdtPerpetuals(cancellationToken), settings.Pairs)];
        SetNumbers[] totals = [.. plans.Select(_ => SetNumbers.Zero)];
        List<(string Pair, SetNumbers Numbers)>[] byPair = [.. plans.Select(_ => new List<(string, SetNumbers)>())];
        request.Progress?.Begin(pairs.Length);
        foreach (Instrument pair in pairs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<PairSetResult> results = await PairBacktestRun.RunAsync(history, pair, backtest.Kind, plans, cancellationToken);
            for (int index = 0; index < results.Count; index++)
            {
                totals[index] += results[index].Numbers;
                byPair[index].Add((pair.Symbol, results[index].Numbers));
            }

            request.Progress?.Advance();
        }

        TimeSpan read = timed.Elapsed;
        TimeSpan compute = clock.Elapsed - read;
        var outcome = new BacktestOutcome(
            [
                .. plans.Select((plan, index) => (
                    plan.Set,
                    SetSummary.Of(totals[index]),
                    (IReadOnlyList<PairSummary>)[.. PairNumbers.Ordered(byPair[index]).Select(pair => new PairSummary(pair.Pair, SetSummary.Of(pair.Numbers)))]))
            ],
            EngineVersion.Number,
            hash.Result(),
            read.TotalSeconds,
            compute > TimeSpan.Zero ? compute.TotalSeconds : 0,
            pairs.Length);
        await backtests.FinishAsync(request.Assignment, outcome, cancellationToken);
        logger.LogInformation("{Line}", $"Backtest {backtest.Id}: {pairs.Length} pairs, {plans.Count} sets, read {read.TotalSeconds:F1} s, computed {compute.TotalSeconds:F1} s");
        return new ExecuteBacktestResult(outcome);
    }

    // The pairs of the settings that are listed, or every listed pair, in the order of their symbols.
    private static IEnumerable<Instrument> Pairs(IReadOnlyList<Instrument> listed, IReadOnlyList<string>? chosen)
    {
        IEnumerable<Instrument> pairs = chosen is null ? listed : listed.Where(pair => chosen.Contains(pair.Symbol, StringComparer.Ordinal));
        return pairs.OrderBy(pair => pair.Symbol, StringComparer.Ordinal);
    }
}
