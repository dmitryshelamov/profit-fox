using ProfitFox.Bench.Domain.Ledger;
using ProfitFox.Bench.Infrastructure.Ledger;

namespace ProfitFox.Tests.Unit.Bench.Ledger;

/// <summary>
///     Spec hypothesis-bench: the ledger of runs and its rules (task 7.4).
/// </summary>
public sealed class RunLedgerTests
{
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private string _path = "";

    [SetUp]
    public void CreatePath()
    {
        _path = Path.Combine(Path.GetTempPath(), $"profit-fox-ledger-{Guid.NewGuid():N}", "ledger.jsonl");
    }

    [Test]
    public void Lines_are_appended_and_read_back()
    {
        var ledger = new LedgerFile(_path);
        ledger.Append(Entry("H0001", RunStatus.InsufficientData) with { Power = 0.42 });
        ledger.Append(Entry("H0002", RunStatus.Rejected) with { T = 1.5 });

        IReadOnlyList<LedgerEntry> entries = ledger.Entries();

        Assert.That(entries.Select(entry => (entry.Hypothesis, entry.Status)), Is.EqualTo(new[] { ("H0001", RunStatus.InsufficientData), ("H0002", RunStatus.Rejected) }));
        Assert.That(entries[0].Power, Is.EqualTo(0.42));
        Assert.That(File.ReadAllLines(_path), Has.Length.EqualTo(2));
    }

    [Test]
    public void A_changed_file_under_the_same_id_is_refused()
    {
        var ledger = new LedgerFile(_path);
        ledger.Append(Entry("H0001", RunStatus.InsufficientData));

        Assert.That(() => LedgerRules.CheckCanRun(ledger.Entries(), "H0001", new string('b', 64), "exploration", parity: false), Throws.InstanceOf<LedgerException>()
            .With.Message.Contains("new hypothesis with a new id"));
    }

    [Test]
    public void A_verdict_is_final_but_too_little_data_may_be_repeated()
    {
        var ledger = new LedgerFile(_path);
        ledger.Append(Entry("H0001", RunStatus.InsufficientData));
        LedgerRules.CheckCanRun(ledger.Entries(), "H0001", Hash, "exploration", parity: false);
        ledger.Append(Entry("H0001", RunStatus.Rejected));

        Assert.That(() => LedgerRules.CheckCanRun(ledger.Entries(), "H0001", Hash, "exploration", parity: false), Throws.InstanceOf<LedgerException>()
            .With.Message.Contains("already has the verdict rejected"));
    }

    [Test]
    public void The_holdout_opens_only_after_exploration_passed_and_only_once()
    {
        var ledger = new LedgerFile(_path);
        Assert.That(() => LedgerRules.CheckCanRun(ledger.Entries(), "H0001", Hash, "holdout", parity: false), Throws.InstanceOf<LedgerException>());

        ledger.Append(Entry("H0001", RunStatus.Passed));
        LedgerRules.CheckCanRun(ledger.Entries(), "H0001", Hash, "holdout", parity: false);
        ledger.Append(Entry("H0001", RunStatus.InsufficientData, "holdout"));
        LedgerRules.CheckCanRun(ledger.Entries(), "H0001", Hash, "holdout", parity: false);
        ledger.Append(Entry("H0001", RunStatus.Passed, "holdout"));

        Assert.That(() => LedgerRules.CheckCanRun(ledger.Entries(), "H0001", Hash, "holdout", parity: false), Throws.InstanceOf<LedgerException>());
    }

    [Test]
    public void Failed_and_parity_runs_are_not_attempts()
    {
        var ledger = new LedgerFile(_path);
        ledger.Append(Entry("H0001", RunStatus.Rejected));
        ledger.Append(Entry("H0002", RunStatus.Failed));
        ledger.Append(Entry("H0003", RunStatus.Parity));
        ledger.Append(Entry("H0004", RunStatus.InsufficientData));
        ledger.Append(Entry("H0005", RunStatus.Passed, "holdout"));

        Assert.That(LedgerRules.Attempts(ledger.Entries(), "H0006", "exploration"), Is.EqualTo(2));
        Assert.That(LedgerRules.Attempts(ledger.Entries(), "H0001", "exploration"), Is.EqualTo(1));
    }

    [Test]
    public void A_correction_takes_a_run_out_of_the_rules()
    {
        var ledger = new LedgerFile(_path);
        LedgerEntry wrong = Entry("H0001", RunStatus.Rejected);
        ledger.Append(wrong);
        ledger.Append(Entry("H0001", RunStatus.Failed) with { Corrects = wrong.RunId, Reason = "the funding node priced payments wrong" });

        LedgerRules.CheckCanRun(ledger.Entries(), "H0001", Hash, "exploration", parity: false);
        Assert.That(LedgerRules.Attempts(ledger.Entries(), "H0002", "exploration"), Is.EqualTo(1));
    }

    [Test]
    public void A_parity_run_needs_only_the_same_hash()
    {
        var ledger = new LedgerFile(_path);
        ledger.Append(Entry("H0001", RunStatus.Rejected));

        LedgerRules.CheckCanRun(ledger.Entries(), "H0001", Hash, "exploration", parity: true);
        Assert.That(() => LedgerRules.CheckCanRun(ledger.Entries(), "H0001", new string('c', 64), "exploration", parity: true), Throws.InstanceOf<LedgerException>());
    }

    [Test]
    public void The_ledger_and_the_reports_do_not_make_the_tree_dirty()
    {
        const string porcelain = " M bench/ledger.jsonl\n?? bench/reports/H0001/run.md\n M backend/src/engine/ProfitFox.Engine.Strategy/Nodes/Data/FundingRateNode.cs\n";

        Assert.That(GitRepository.Changed(porcelain), Is.EqualTo(new[] { "backend/src/engine/ProfitFox.Engine.Strategy/Nodes/Data/FundingRateNode.cs" }));
        Assert.That(GitRepository.Changed(""), Is.Empty);
    }

    [Test]
    public void The_signals_of_the_constructor_do_not_make_the_tree_dirty()
    {
        const string porcelain = "?? bench/signals/funding-high.json\n M bench/signals/price-drop.json\nR  bench/signals/a.json -> bench/signals/b.json\n";

        Assert.That(GitRepository.Changed(porcelain), Is.Empty);
    }

    [Test]
    public void A_hypothesis_beside_a_signal_still_makes_the_tree_dirty()
    {
        const string porcelain = "?? bench/signals/funding-high.json\n?? bench/hypotheses/H0002-x/hypothesis.json\n";

        Assert.That(GitRepository.Changed(porcelain), Is.EqualTo(new[] { "bench/hypotheses/H0002-x/hypothesis.json" }));
    }

    [Test]
    public void A_file_named_like_the_signals_folder_still_makes_the_tree_dirty()
    {
        const string porcelain = "?? bench/signals.json\n";

        Assert.That(GitRepository.Changed(porcelain), Is.EqualTo(new[] { "bench/signals.json" }));
    }

    private static LedgerEntry Entry(string hypothesis, string status, string period = "exploration")
    {
        return new LedgerEntry
        {
            RunId = Guid.NewGuid().ToString("N"),
            Time = DateTimeOffset.UtcNow,
            Hypothesis = hypothesis,
            Hash = Hash,
            Commit = "c0ffee",
            Period = period,
            Status = status
        };
    }
}
