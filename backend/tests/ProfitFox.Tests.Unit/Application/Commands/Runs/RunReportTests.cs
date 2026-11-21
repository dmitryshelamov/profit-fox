using System.Text.Json.Nodes;

using ProfitFox.Application.Commands.Runs;
using ProfitFox.Domain.Services.Execution;
using ProfitFox.Domain.Services.Hypotheses;
using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.Services.Strategies;
using ProfitFox.Domain.Services.Verdicts;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Ledger;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;
using ProfitFox.Domain.ValueObjects.Verdicts;
using ProfitFox.Domain.ValueObjects.Verdicts.Enums;
using ProfitFox.Tests.Unit.Domain.Services.Hypotheses;

using static ProfitFox.Tests.Unit.Domain.Services.Runs.Designs;

namespace ProfitFox.Tests.Unit.Application.Commands.Runs;

/// <summary>
///     Spec hypothesis-bench: the report of a run (task 7.5), on synthetic rows; and the number of events skipped while
///     the position was open (change add-strategy-graph, task 4.4).
/// </summary>
public sealed class RunReportTests
{
    private static readonly long First = Blocks.Of(Utc(2026, 1, 5), ProfitFox.Domain.ValueObjects.Hypotheses.Enums.TimeBlock.Week);
    private static readonly (long Block, double Mean, int Events)[] Pseudo = [(First, 0.0004, 3), (First + 1, -0.0002, 2)];

    [Test]
    public void A_report_of_too_little_data_has_no_outcomes_of_events()
    {
        Verdict verdict = VerdictRules.Decide(null, [0.0004, -0.0002], 0.003, 1, events: 5);

        string text = RunReport.Text(Exploration(), Entry(RunStatus.InsufficientData, verdict), verdict, Rows(), Pseudo, blocks: null, pairs: 500, pairsWithEvents: 2, skipped: 0);

        Assert.That(text, Does.Contain(RunReport.SurvivorsWarning));
        Assert.That(text, Does.Contain("| Версия гипотезы | 1 |"));
        Assert.That(text, Does.Contain("| Версия движка | 1 |"));
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

        string text = RunReport.Text(Exploration(), Entry(RunStatus.Rejected, verdict), verdict, Rows(), Pseudo, blocks, pairs: 500, pairsWithEvents: 2, skipped: 0);

        Assert.That(text, Does.Contain("**Отвергнута**: t 1.20 < 3.00."));
        Assert.That(text, Does.Contain("| События | 0.00700 |"));
        Assert.That(text, Does.Contain("| 2026-01-12 | 2 | 0.00100 |"));
    }

    [Test]
    public void A_second_event_inside_the_time_of_the_first_is_one_skipped_event_of_the_report()
    {
        // H0001 with a short for a day: the events of Monday 12:00 and 16:00, without a stop and a target.
        JsonObject file = JsonNode.Parse(HypothesisReaderTests.H0001Text)!.AsObject();
        file["plan"]!["horizon"] = "1d";
        RunDesign design = Exploration(HypothesisReader.Parse(file.ToJsonString()));
        StrategyStep written = new StrategyExecutor(design.Hypothesis.Strategy).BeforeCandles(DateTimeOffset.UnixEpoch);
        StrategyStep[] orders = [.. new[] { Utc(2026, 1, 5, 12), Utc(2026, 1, 5, 16) }.Select(moment => written with { Moment = moment, Entry = design.Hypothesis.Strategy.Entries[0].Side })];
        var evaluation = new PairEvaluation("BTCUSDT", new PairSteps(orders, written), design, withEventOutcomes: false);
        List<EventRow> rows = evaluation.Evaluate(First, Week(design));
        Verdict verdict = VerdictRules.Decide(null, [0.0004, -0.0002], 0.003, 1, events: 1);

        string text = RunReport.Text(design, Entry(RunStatus.InsufficientData, verdict), verdict, rows, Pseudo, blocks: null, pairs: 500, pairsWithEvents: 1, evaluation.Skipped);

        Assert.That(rows.Select(row => row.Moment), Is.EqualTo(new[] { Utc(2026, 1, 5, 12) }));
        Assert.That(text, Does.Contain("| Событий пропущено при открытой позиции | 1 |"));
        Assert.That(text, Does.Contain("| учтены | 1 | 20 |"), "the skipped event is not among the trades, counted or left out");
    }

    [Test]
    public void The_report_of_H0001_shows_no_skipped_events()
    {
        Verdict verdict = VerdictRules.Decide(null, [0.0004, -0.0002], 0.003, 1, events: 5);

        string text = RunReport.Text(Exploration(), Entry(RunStatus.InsufficientData, verdict), verdict, Rows(), Pseudo, blocks: null, pairs: 500, pairsWithEvents: 2, skipped: 0);

        Assert.That(text, Does.Contain("| Событий пропущено при открытой позиции | 0 |"));
    }

    [Test]
    public void An_order_that_gave_no_plan_has_its_row_among_the_trades_left_out()
    {
        Verdict verdict = VerdictRules.Decide(null, [0.0004, -0.0002], 0.003, 1, events: 5);
        EventRow[] rows = [.. Rows(), new EventRow("SOLUSDT", Utc(2026, 1, 6, 8), First, TradeStatus.Plan, null, null, [], 0, null, null)];

        string text = RunReport.Text(Exploration(), Entry(RunStatus.InsufficientData, verdict), verdict, rows, Pseudo, blocks: null, pairs: 500, pairsWithEvents: 3, skipped: 0);

        Assert.That(text, Does.Contain("| недопустимая заявка (`plan`) | 1 | 0 |"));
        Assert.That(text, Does.Contain("| вне покрытия фандинга (`funding`) | 0 | 0 |"));
    }

    // The minutes of the first week as a run reads them, at a price of 100.
    private static MinutePath Week(RunDesign design)
    {
        (DateTimeOffset from, DateTimeOffset to) = design.Span(First);
        double[] price = [.. Enumerable.Repeat(100.0, (int)(to - from).TotalMinutes)];
        return new MinutePath(from, price, price, price, price, [.. Enumerable.Repeat(true, price.Length)], [], from, to.AddDays(1));
    }

    private static LedgerEntry Entry(string status, Verdict verdict)
    {
        return new LedgerEntry
        {
            RunId = "0123456789abcdef0123456789abcdef",
            Time = Utc(2026, 11, 18, 12),
            Hypothesis = "H0001",
            Hash = H0001.Hash,
            Version = 1,
            EngineVersion = 1,
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
        var excluded = TradeOutcome.Excluded(TradeStatus.Coverage);
        return
        [
            new EventRow("BTCUSDT", Utc(2026, 1, 5, 8), First, TradeStatus.Ok, null, fields, [new RandomTrade(Utc(2026, 1, 6), excluded)], 2, random, 0.0004),
            new EventRow("ETHUSDT", Utc(2026, 1, 13, 8), First + 1, TradeStatus.NoTrade, null, null, [], 0, null, null)
        ];
    }
}
