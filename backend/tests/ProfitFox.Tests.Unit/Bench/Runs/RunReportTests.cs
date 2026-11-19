using ProfitFox.Bench.Application.Runs;
using ProfitFox.Bench.Domain.Ledger;
using ProfitFox.Bench.Domain.Runs;
using ProfitFox.Bench.Domain.Verdicts;
using ProfitFox.Engine.Simulation.Trades;

using static ProfitFox.Tests.Unit.Bench.Runs.Designs;

namespace ProfitFox.Tests.Unit.Bench.Runs;

/// <summary>
///     Spec hypothesis-bench: the report of a run (task 7.5), on synthetic rows.
/// </summary>
public sealed class RunReportTests
{
    private static readonly long First = Blocks.Of(Utc(2026, 1, 5), ProfitFox.Bench.Domain.Hypotheses.TimeBlock.Week);
    private static readonly (long Block, double Mean, int Events)[] Pseudo = [(First, 0.0004, 3), (First + 1, -0.0002, 2)];

    [Test]
    public void A_report_of_too_little_data_has_no_outcomes_of_events()
    {
        Verdict verdict = VerdictRules.Decide(null, [0.0004, -0.0002], 0.003, 1, events: 5);

        string text = RunReport.Text(Exploration(), Entry(RunStatus.InsufficientData, verdict), verdict, Rows(), Pseudo, blocks: null, pairs: 500, pairsWithEvents: 2);

        Assert.That(text, Does.Contain(RunReport.SurvivorsWarning));
        Assert.That(text, Does.Contain("**Данных мало**: 2 blocks, need 20."));
        Assert.That(text, Does.Contain("Исходы событий не читались"));
        Assert.That(text, Does.Not.Contain("Средние по учтённым событиям"));
        Assert.That(text, Does.Not.Contain("0.00700"), "the outcome of an event must not reach the report");
        Assert.That(text, Does.Contain("| 2026-01-05 | 3 | 0.00040 |"));
    }

    [Test]
    public void A_report_with_a_verdict_shows_the_means_and_the_blocks()
    {
        var verdict = new Verdict(VerdictKind.Rejected, "t 1.20 < 3.00", 1, 3.0, 0.003, 2, 5, 0.9, 0.001, T: 1.2, Mean: 0.002, FirstHalfMean: 0.003, SecondHalfMean: 0.001);
        (long, double, int)[] blocks = [(First, 0.003, 3), (First + 1, 0.001, 2)];

        string text = RunReport.Text(Exploration(), Entry(RunStatus.Rejected, verdict), verdict, Rows(), Pseudo, blocks, pairs: 500, pairsWithEvents: 2);

        Assert.That(text, Does.Contain("**Отвергнута**: t 1.20 < 3.00."));
        Assert.That(text, Does.Contain("| События | 0.00700 |"));
        Assert.That(text, Does.Contain("| 2026-01-12 | 2 | 0.00100 |"));
    }

    private static LedgerEntry Entry(string status, Verdict verdict)
    {
        return new LedgerEntry
        {
            RunId = "0123456789abcdef0123456789abcdef",
            Time = Utc(2026, 11, 18, 12),
            Hypothesis = "H0001",
            Hash = H0001.Hash,
            Commit = "c0ffee",
            Period = "exploration",
            Status = status,
            DataHash = "d4",
            Attempts = 1,
            Reason = verdict.Reason,
            RequiredT = verdict.RequiredT,
            Power = verdict.Power,
            Blocks = verdict.Blocks,
            Events = verdict.Events,
            T = verdict.T,
            ReadSeconds = 12
        };
    }

    private static EventRow[] Rows()
    {
        var fields = new TradeFields(0.007, 0.008, 0.0011, 0.001, 0.0011, 0.007);
        var random = new TradeFields(0.001, 0.002, 0.0011, 0.001, 0.0001, 0.001);
        TradeOutcome excluded = TradeOutcome.Excluded(TradeStatus.Coverage);
        return
        [
            new EventRow("BTCUSDT", Utc(2026, 1, 5, 8), First, TradeStatus.Ok, null, fields, [new RandomTrade(Utc(2026, 1, 6), excluded)], 2, random, 0.0004),
            new EventRow("ETHUSDT", Utc(2026, 1, 13, 8), First + 1, TradeStatus.NoTrade, null, null, [], 0, null, null)
        ];
    }
}
