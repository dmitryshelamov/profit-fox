using System.Diagnostics;
using ProfitFox.Bench.Application.Hypotheses;
using ProfitFox.Bench.Application.MarketData;
using ProfitFox.Bench.Domain.Hypotheses;
using ProfitFox.Bench.Domain.Ledger;
using ProfitFox.Bench.Domain.MarketData;
using ProfitFox.Bench.Domain.Runs;
using ProfitFox.Bench.Domain.Signals;
using ProfitFox.Bench.Domain.Verdicts;

namespace ProfitFox.Bench.Application.Runs;

/// <summary>
///     A run of a hypothesis (spec hypothesis-bench): the file and the ledger are checked before any history is read; then
///     pair by pair the signal is computed on the candles of the timeframe, the events of the period get their random
///     entries, and their trades are computed on the minutes of their blocks. Only small rows per event stay in memory. The
///     verdict looks at the outcomes of the events only after the blind power check; a parity run never computes them.
///     Every run ends with a line in the ledger, a failed one included.
/// </summary>
public sealed class HypothesisRunner(
    HypothesisCatalog hypotheses,
    IHistorySource source,
    IInstrumentCatalog instruments,
    IRepository repository,
    IRunLedger ledger,
    IRunReports reports,
    IParityDumps dumps,
    TextWriter log)
{
    public async Task<RunResult> Run(RunOptions options, CancellationToken cancellation)
    {
        Hypothesis hypothesis = hypotheses.Read(options.HypothesisFolder);
        IReadOnlyList<string> uncommitted = repository.Uncommitted();
        if (uncommitted.Count > 0)
        {
            throw new UncommittedChangesException(uncommitted);
        }

        string period = Periods.Name(options.Period);
        IReadOnlyList<LedgerEntry> entries = ledger.Entries();
        LedgerRules.CheckCanRun(entries, hypothesis.Id, hypothesis.Hash, period, options.Parity);
        (DateTimeOffset start, DateTimeOffset end) = Periods.Span(options.Period, options.Today);
        var design = new RunDesign(hypothesis, options.Period, start, end);
        int attempts = LedgerRules.Attempts(entries, hypothesis.Id, period);
        var entry = new LedgerEntry
        {
            RunId = Guid.NewGuid().ToString("N"),
            Time = DateTimeOffset.UtcNow,
            Hypothesis = hypothesis.Id,
            Hash = hypothesis.Hash,
            Commit = repository.Commit(),
            Period = period,
            Status = RunStatus.Failed,
            Attempts = attempts
        };

        try
        {
            return await Run(design, options, entry, cancellation);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ledger.Append(entry with { Status = RunStatus.Failed, Reason = exception.Message });
            throw;
        }
    }

    private async Task<RunResult> Run(RunDesign design, RunOptions options, LedgerEntry entry, CancellationToken cancellation)
    {
        Hypothesis hypothesis = design.Hypothesis;
        var clock = Stopwatch.StartNew();
        using var hash = new HistoryHash();
        var history = new MarketHistory(source, design.PeriodEnd, hash);
        IReadOnlyList<Instrument> pairs = await instruments.LinearUsdtPerpetuals(cancellation);
        using IParityDump? dump = options.DumpFolder is null ? null : dumps.Open(options.DumpFolder);
        List<EventRow> rows = [];
        int pairsWithEvents = 0;
        foreach (Instrument pair in pairs)
        {
            PairData data = await Pair(pair, design, history, withEventOutcomes: !options.Parity, cancellation);
            rows.AddRange(data.Rows);
            pairsWithEvents += data.Rows.Count > 0 ? 1 : 0;
            dump?.Write(data, withEventOutcomes: !options.Parity);
            if (rows.Count > 0 && pairsWithEvents % 50 == 0 && data.Rows.Count > 0)
            {
                log.WriteLine($"{pairsWithEvents} pairs with events, {rows.Count} events");
            }
        }

        double readSeconds = clock.Elapsed.TotalSeconds;
        IReadOnlyList<(long Block, double Mean, int Events)> pseudo = Evidence.PseudoBlockMeans(rows);
        double[] pseudoMeans = [.. pseudo.Select(block => block.Mean)];
        int usable = rows.Count(row => row.Usable);
        Verdict? verdict = null;
        IReadOnlyList<(long Block, double Mean, int Events)>? blocks = null;
        LedgerEntry result;
        if (options.Parity)
        {
            (double power, _, double requiredT) = VerdictRules.PlannedPower(pseudoMeans, hypothesis.MinEffect, entry.Attempts ?? 1);
            result = entry with
            {
                Status = RunStatus.Parity,
                DataHash = hash.Result(),
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
            bool enough = VerdictRules.EnoughPower(pseudoMeans, hypothesis.MinEffect, entry.Attempts ?? 1);
            blocks = enough ? Evidence.BlockMeans(rows) : null;
            verdict = VerdictRules.Decide(blocks?.Select(block => block.Mean).ToArray(), pseudoMeans, hypothesis.MinEffect, entry.Attempts ?? 1, usable);
            result = entry with
            {
                Status = verdict.Kind switch
                {
                    VerdictKind.Passed => RunStatus.Passed,
                    VerdictKind.Rejected => RunStatus.Rejected,
                    _ => RunStatus.InsufficientData
                },
                DataHash = hash.Result(),
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

        dump?.Finish(pseudo, blocks, result);
        ledger.Append(result);
        string report = reports.Write(design, result, RunReport.Text(design, result, verdict, rows, pseudo, blocks, pairs.Count, pairsWithEvents));
        return new RunResult(result, verdict, report, rows);
    }

    private static async Task<PairData> Pair(Instrument pair, RunDesign design, MarketHistory history, bool withEventOutcomes, CancellationToken cancellation)
    {
        Hypothesis hypothesis = design.Hypothesis;
        HistoryRead<HistoryCandle> candles = await history.Candles(pair.Id, hypothesis.Timeframe, Periods.DataStart, design.PeriodEnd, cancellation);
        HistoryRead<HistoryPayment> funding = design.Period == Period.NewData
            ? await history.Funding(pair.Id, Periods.DataStart, design.PeriodEnd, cancellation)
            : await history.FundingThrough(pair.Id, Periods.DataStart, design.PeriodEnd, cancellation);

        List<DateTimeOffset> events = [.. PairEvents.Of(hypothesis.Signal, hypothesis.Timeframe, pair.Symbol, candles.Items, funding.Items).Where(design.InPeriod)];
        if (events.Count == 0)
        {
            return new PairData(pair, candles, funding, [], []);
        }

        IReadOnlyList<DateTimeOffset[]> randoms = RandomEntries.Draw(pair.Symbol, events, design);
        List<EventRow> rows = [];
        List<(DateTimeOffset, DateTimeOffset, HistoryRead<HistoryCandle>)> spans = [];
        foreach (IGrouping<long, int> block in Enumerable.Range(0, events.Count).GroupBy(index => Blocks.Of(events[index], hypothesis.Block)).OrderBy(group => group.Key))
        {
            (DateTimeOffset from, DateTimeOffset to) = design.Span(block.Key);
            HistoryRead<HistoryCandle> minutes = await history.Candles(pair.Id, TimeSpan.FromMinutes(1), from, to, cancellation);
            spans.Add((from, to, minutes));
            int[] indexes = [.. block.OrderBy(index => events[index])];
            rows.AddRange(PairEvaluation.Evaluate(
                pair.Symbol,
                block.Key,
                MinutePaths.Build(from, to, minutes, funding),
                [.. indexes.Select(index => events[index])],
                [.. indexes.Select(index => randoms[index])],
                design,
                withEventOutcomes));
        }

        return new PairData(pair, candles, funding, spans, rows);
    }
}
