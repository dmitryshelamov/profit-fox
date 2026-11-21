using System.Diagnostics;

using MediatR;

using Microsoft.Extensions.Logging;

using ProfitFox.Application.Commands.Runs.Base;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Queries.MarketData;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ledger;
using ProfitFox.Domain.Services.MarketData;
using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.Services.Strategies;
using ProfitFox.Domain.Services.Verdicts;
using ProfitFox.Domain.ValueObjects.Hypotheses;
using ProfitFox.Domain.ValueObjects.Ledger;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Domain.ValueObjects.Runs.Enums;
using ProfitFox.Domain.ValueObjects.Verdicts;
using ProfitFox.Domain.ValueObjects.Verdicts.Enums;

namespace ProfitFox.Application.Commands.Runs.ExecuteRun;

/// <summary>
///     A run of a hypothesis version (spec hypothesis-bench): the version and the ledger are checked before any history is
///     read; then pair by pair the strategy of the hypothesis is executed on the candles of the timeframe, and its orders of
///     the period go through the rule of one position per pair on the minutes of their blocks: an event that opens a cycle
///     gets its random entries, an event that comes while the position is open is skipped and counted. Only small rows per
///     event stay in memory. The verdict looks at the outcomes of the events only after the blind power check; a parity run never computes
///     them. The attempts, the verdict and the report are made when the ledger decides the run, so a run sees the verdicts of
///     the runs that ended before it. Every run ends with a line in the ledger, a failed one included.
/// </summary>
internal sealed class ExecuteRunCommandHandler(
    IHypothesisRepository hypotheses,
    IHistorySource source,
    IInstrumentCatalog instruments,
    ILedgerRepository ledger,
    ILogger<ExecuteRunCommandHandler> logger) : IRequestHandler<ExecuteRunCommand, ExecuteRunResult>
{
    /// <exception cref="OperationCanceledException">The run was cancelled; nothing is written.</exception>
    /// <exception cref="LeaseLostException">A run of the queue ended after its Worker lost the lease; nothing is written.</exception>
    public async Task<ExecuteRunResult> Handle(ExecuteRunCommand request, CancellationToken cancellationToken)
    {
        Hypothesis hypothesis = await RunHypotheses.ReadAsync(hypotheses, request.Hypothesis, request.Version, cancellationToken);
        string period = Periods.Name(request.Period);
        (DateTimeOffset start, DateTimeOffset end) = Periods.Span(request.Period, request.Today);
        var design = new RunDesign(hypothesis, request.Period, start, end);
        LedgerEntry entry = RunLines.Line(request, hypothesis, period);
        if (request.Assignment is null)
        {
            await RunLines.CheckCanRunAsync(ledger, hypothesis, period, request.Parity, cancellationToken);
        }

        try
        {
            if (request.Assignment is not null)
            {
                await RunLines.CheckCanRunAsync(ledger, hypothesis, period, request.Parity, cancellationToken);
            }

            return await Run(design, request, entry, cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or LeaseLostException) && !cancellationToken.IsCancellationRequested)
        {
            await RunLines.CloseAsync(ledger, hypothesis, entry, RunStatus.Failed, exception.Message, request.Assignment);
            throw;
        }
    }

    private async Task<ExecuteRunResult> Run(RunDesign design, ExecuteRunCommand request, LedgerEntry entry, CancellationToken cancellationToken)
    {
        Hypothesis hypothesis = design.Hypothesis;
        var clock = Stopwatch.StartNew();
        using var hash = new HistoryHash();
        var history = new MarketHistory(source, design.PeriodEnd, hash);
        IReadOnlyList<Instrument> pairs = await instruments.LinearUsdtPerpetuals(cancellationToken);
        List<EventRow> rows = [];
        int pairsWithEvents = 0;
        int skipped = 0;
        request.Progress?.Begin(pairs.Count);
        foreach (Instrument pair in pairs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PairData data = await Pair(pair, design, history, withEventOutcomes: !request.Parity, cancellationToken);
            rows.AddRange(data.Rows);
            skipped += data.Skipped;
            pairsWithEvents += data.Rows.Count > 0 ? 1 : 0;
            request.Progress?.Advance();
            if (rows.Count > 0 && pairsWithEvents % 50 == 0 && data.Rows.Count > 0)
            {
                logger.LogInformation("{Line}", $"{pairsWithEvents} pairs with events, {rows.Count} events");
            }
        }

        double readSeconds = clock.Elapsed.TotalSeconds;
        string dataHash = hash.Result();
        IReadOnlyList<(long Block, double Mean, int Events)> pseudo = Evidence.PseudoBlockMeans(rows);
        double[] pseudoMeans = [.. pseudo.Select(block => block.Mean)];
        int usable = rows.Count(row => row.Usable);
        Verdict? verdict = null;
        string report = "";
        LedgerEntry written = await ledger.DecideAsync(
            entries =>
            {
                LedgerRules.CheckCanRun(entries, hypothesis.Id, hypothesis.Version, entry.Period, request.Parity);
                int attempts = LedgerRules.Attempts(entries, hypothesis.Id, hypothesis.Version, entry.Period);
                IReadOnlyList<(long Block, double Mean, int Events)>? blocks = null;
                LedgerEntry result;
                if (request.Parity)
                {
                    (double power, _, double requiredT) = VerdictRules.PlannedPower(pseudoMeans, hypothesis.MinEffect, attempts);
                    result = entry with
                    {
                        Status = RunStatus.Parity,
                        Attempts = attempts,
                        DataHash = dataHash,
                        Reason = "parity run: event outcomes not computed",
                        RequiredT = requiredT,
                        Power = power,
                        Blocks = pseudoMeans.Length,
                        Events = usable,
                        ReadSeconds = readSeconds
                    };
                }
                else
                {
                    bool enough = VerdictRules.EnoughPower(pseudoMeans, hypothesis.MinEffect, attempts);
                    blocks = enough ? Evidence.BlockMeans(rows) : null;
                    verdict = VerdictRules.Decide(blocks?.Select(block => block.Mean).ToArray(), pseudoMeans, hypothesis.MinEffect, attempts, usable);
                    result = entry with
                    {
                        Status = verdict.Kind switch
                        {
                            VerdictKind.Passed => RunStatus.Passed,
                            VerdictKind.Rejected => RunStatus.Rejected,
                            _ => RunStatus.InsufficientData
                        },
                        Attempts = attempts,
                        DataHash = dataHash,
                        Reason = verdict.Reason,
                        RequiredT = verdict.RequiredT,
                        Power = verdict.Power,
                        Blocks = verdict.Blocks,
                        Events = verdict.Events,
                        T = verdict.T,
                        Mean = verdict.Mean,
                        FirstHalfMean = verdict.FirstHalfMean,
                        SecondHalfMean = verdict.SecondHalfMean,
                        ReadSeconds = readSeconds
                    };
                }

                report = RunReport.Text(design, result, verdict, rows, pseudo, blocks, pairs.Count, pairsWithEvents, skipped);
                return new Decision(result, report);
            },
            request.Assignment,
            cancellationToken);
        return new ExecuteRunResult(written, verdict, report, rows, skipped);
    }

    private static async Task<PairData> Pair(Instrument pair, RunDesign design, MarketHistory history, bool withEventOutcomes, CancellationToken cancellationToken)
    {
        Hypothesis hypothesis = design.Hypothesis;
        HistoryRead<HistoryCandle> candles = await history.Candles(pair.Id, hypothesis.Timeframe, Periods.DataStart, design.PeriodEnd, cancellationToken);
        HistoryRead<HistoryPayment> funding = design.Period == Period.NewData
            ? await history.Funding(pair.Id, Periods.DataStart, design.PeriodEnd, cancellationToken)
            : await history.FundingThrough(pair.Id, Periods.DataStart, design.PeriodEnd, cancellationToken);

        var steps = PairSteps.Of(hypothesis.Strategy, hypothesis.Timeframe, pair.Symbol, candles.Items, funding.Items);
        var evaluation = new PairEvaluation(pair.Symbol, steps, design, withEventOutcomes);
        List<EventRow> rows = [];
        List<(DateTimeOffset, DateTimeOffset, HistoryRead<HistoryCandle>)> spans = [];
        foreach (long block in evaluation.Blocks)
        {
            (DateTimeOffset from, DateTimeOffset to) = design.Span(block);
            HistoryRead<HistoryCandle> minutes = await history.Candles(pair.Id, TimeSpan.FromMinutes(1), from, to, cancellationToken);
            spans.Add((from, to, minutes));
            rows.AddRange(evaluation.Evaluate(block, MinutePaths.Build(from, to, minutes, funding)));
        }

        return new PairData(pair, candles, funding, spans, rows, evaluation.Skipped);
    }
}
