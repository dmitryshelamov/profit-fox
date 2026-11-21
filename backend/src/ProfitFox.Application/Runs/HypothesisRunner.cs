using System.Diagnostics;

using ProfitFox.Application.Hypotheses;
using ProfitFox.Application.MarketData;
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

namespace ProfitFox.Application.Runs;

/// <summary>
///     A run of a hypothesis version (spec hypothesis-bench): the version and the ledger are checked before any history is
///     read; then pair by pair the strategy of the hypothesis is executed on the candles of the timeframe, and its orders of
///     the period go through the rule of one position per pair on the minutes of their blocks: an event that opens a cycle
///     gets its random entries, an event that comes while the position is open is skipped and counted. Only small rows per
///     event stay in memory. The verdict looks at the outcomes of the events only after the blind power check; a parity run never computes
///     them. The attempts, the verdict and the report are made when the ledger decides the run, so a run sees the verdicts of
///     the runs that ended before it. Every run ends with a line in the ledger, a failed one included.
/// </summary>
public sealed class HypothesisRunner(
    HypothesisCatalog hypotheses,
    IHistorySource source,
    IInstrumentCatalog instruments,
    IRunLedger ledger,
    TextWriter log)
{
    /// <summary>
    ///     Runs the hypothesis. A run that is not of the queue is refused by the rules of the ledger before anything is written;
    ///     a run of the queue (<see cref="RunOptions.Assignment" />) ends with a line in every case, a refusal included, since
    ///     its row would otherwise never end. <paramref name="progress" /> gets the pairs as they pass.
    /// </summary>
    /// <exception cref="OperationCanceledException">The run was cancelled; nothing is written.</exception>
    /// <exception cref="LeaseLostException">A run of the queue ended after its Worker lost the lease; nothing is written.</exception>
    public async Task<RunResult> Run(RunOptions options, CancellationToken cancellation, RunProgress? progress = null)
    {
        Hypothesis hypothesis = await hypotheses.Read(options.Hypothesis, options.Version, cancellation);
        string period = Periods.Name(options.Period);
        (DateTimeOffset start, DateTimeOffset end) = Periods.Span(options.Period, options.Today);
        var design = new RunDesign(hypothesis, options.Period, start, end);
        LedgerEntry entry = Line(options, hypothesis, period);
        if (options.Assignment is null)
        {
            await CheckCanRun(hypothesis, period, options.Parity, cancellation);
        }

        try
        {
            if (options.Assignment is not null)
            {
                await CheckCanRun(hypothesis, period, options.Parity, cancellation);
            }

            return await Run(design, options, entry, progress, cancellation);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or LeaseLostException) && !cancellation.IsCancellationRequested)
        {
            await Close(hypothesis, entry, RunStatus.Failed, exception.Message, options.Assignment);
            throw;
        }
    }

    /// <summary>
    ///     Ends a run of the queue without computing it: with <see cref="RunStatus.Failed" /> for a run that was interrupted too
    ///     often, with <see cref="RunStatus.Cancelled" /> for a run its owner stopped. The line has no report.
    /// </summary>
    /// <exception cref="LeaseLostException">The Worker does not hold the lease of the run; nothing is written.</exception>
    public async Task Stop(RunOptions options, string status, string reason, CancellationToken cancellation)
    {
        Hypothesis hypothesis = await hypotheses.Read(options.Hypothesis, options.Version, cancellation);
        await Close(hypothesis, Line(options, hypothesis, Periods.Name(options.Period)), status, reason, options.Assignment);
    }

    private async Task CheckCanRun(Hypothesis hypothesis, string period, bool parity, CancellationToken cancellation)
    {
        LedgerRules.CheckCanRun(await ledger.Entries(cancellation), hypothesis.Id, hypothesis.Version, period, parity);
    }

    private static LedgerEntry Line(RunOptions options, Hypothesis hypothesis, string period)
    {
        return new LedgerEntry
        {
            RunId = options.Assignment?.RunId ?? Guid.NewGuid().ToString("N"),
            Time = DateTimeOffset.UtcNow,
            Hypothesis = hypothesis.Id,
            Hash = hypothesis.Hash,
            Version = hypothesis.Version,
            EngineVersion = EngineVersion.Number,
            Period = period,
            Status = RunStatus.Failed
        };
    }

    // The line of a run that ends without a verdict: it shows the attempts the verdict would have been, and has no report.
    private Task Close(Hypothesis hypothesis, LedgerEntry entry, string status, string reason, RunAssignment? assignment)
    {
        return ledger.Decide(
            entries => new Decision(
                entry with
                {
                    Status = status,
                    Attempts = LedgerRules.Attempts(entries, hypothesis.Id, hypothesis.Version, entry.Period),
                    Reason = reason
                },
                Report: null),
            assignment,
            CancellationToken.None);
    }

    private async Task<RunResult> Run(RunDesign design, RunOptions options, LedgerEntry entry, RunProgress? progress, CancellationToken cancellation)
    {
        Hypothesis hypothesis = design.Hypothesis;
        var clock = Stopwatch.StartNew();
        using var hash = new HistoryHash();
        var history = new MarketHistory(source, design.PeriodEnd, hash);
        IReadOnlyList<Instrument> pairs = await instruments.LinearUsdtPerpetuals(cancellation);
        List<EventRow> rows = [];
        int pairsWithEvents = 0;
        int skipped = 0;
        progress?.Begin(pairs.Count);
        foreach (Instrument pair in pairs)
        {
            cancellation.ThrowIfCancellationRequested();
            PairData data = await Pair(pair, design, history, withEventOutcomes: !options.Parity, cancellation);
            rows.AddRange(data.Rows);
            skipped += data.Skipped;
            pairsWithEvents += data.Rows.Count > 0 ? 1 : 0;
            progress?.Advance();
            if (rows.Count > 0 && pairsWithEvents % 50 == 0 && data.Rows.Count > 0)
            {
                log.WriteLine($"{pairsWithEvents} pairs with events, {rows.Count} events");
            }
        }

        double readSeconds = clock.Elapsed.TotalSeconds;
        string dataHash = hash.Result();
        IReadOnlyList<(long Block, double Mean, int Events)> pseudo = Evidence.PseudoBlockMeans(rows);
        double[] pseudoMeans = [.. pseudo.Select(block => block.Mean)];
        int usable = rows.Count(row => row.Usable);
        Verdict? verdict = null;
        string report = "";
        LedgerEntry written = await ledger.Decide(
            entries =>
            {
                LedgerRules.CheckCanRun(entries, hypothesis.Id, hypothesis.Version, entry.Period, options.Parity);
                int attempts = LedgerRules.Attempts(entries, hypothesis.Id, hypothesis.Version, entry.Period);
                IReadOnlyList<(long Block, double Mean, int Events)>? blocks = null;
                LedgerEntry result;
                if (options.Parity)
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
            options.Assignment,
            cancellation);
        return new RunResult(written, verdict, report, rows, skipped);
    }

    private static async Task<PairData> Pair(Instrument pair, RunDesign design, MarketHistory history, bool withEventOutcomes, CancellationToken cancellation)
    {
        Hypothesis hypothesis = design.Hypothesis;
        HistoryRead<HistoryCandle> candles = await history.Candles(pair.Id, hypothesis.Timeframe, Periods.DataStart, design.PeriodEnd, cancellation);
        HistoryRead<HistoryPayment> funding = design.Period == Period.NewData
            ? await history.Funding(pair.Id, Periods.DataStart, design.PeriodEnd, cancellation)
            : await history.FundingThrough(pair.Id, Periods.DataStart, design.PeriodEnd, cancellation);

        var steps = PairSteps.Of(hypothesis.Strategy, hypothesis.Timeframe, pair.Symbol, candles.Items, funding.Items);
        var evaluation = new PairEvaluation(pair.Symbol, steps, design, withEventOutcomes);
        List<EventRow> rows = [];
        List<(DateTimeOffset, DateTimeOffset, HistoryRead<HistoryCandle>)> spans = [];
        foreach (long block in evaluation.Blocks)
        {
            (DateTimeOffset from, DateTimeOffset to) = design.Span(block);
            HistoryRead<HistoryCandle> minutes = await history.Candles(pair.Id, TimeSpan.FromMinutes(1), from, to, cancellation);
            spans.Add((from, to, minutes));
            rows.AddRange(evaluation.Evaluate(block, MinutePaths.Build(from, to, minutes, funding)));
        }

        return new PairData(pair, candles, funding, spans, rows, evaluation.Skipped);
    }
}
