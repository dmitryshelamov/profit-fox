using ProfitFox.Domain.Ledger;

namespace ProfitFox.Tests.Unit.Bench.Ledger;

/// <summary>
///     Spec hypothesis-bench: the rules of the ledger of runs (task 7.4); the ledger in the database is tested with it.
/// </summary>
public sealed class RunLedgerTests
{
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    [Test]
    public void A_verdict_is_final_for_its_version_but_too_little_data_may_be_repeated()
    {
        List<LedgerEntry> ledger = [Entry("H0001", RunStatus.InsufficientData)];
        LedgerRules.CheckCanRun(ledger, "H0001", 1, "exploration", parity: false);
        ledger.Add(Entry("H0001", RunStatus.Rejected));

        Assert.That(() => LedgerRules.CheckCanRun(ledger, "H0001", 1, "exploration", parity: false), Throws.InstanceOf<LedgerException>()
            .With.Message.Contains("already has the verdict rejected"));
    }

    [Test]
    public void A_new_version_after_a_verdict_may_run()
    {
        List<LedgerEntry> ledger = [Entry("H0001", RunStatus.Rejected)];

        LedgerRules.CheckCanRun(ledger, "H0001", 2, "exploration", parity: false);
    }

    [Test]
    public void The_holdout_opens_only_after_the_version_passed_exploration_and_only_once()
    {
        List<LedgerEntry> ledger = [];
        Assert.That(() => LedgerRules.CheckCanRun(ledger, "H0001", 1, "holdout", parity: false), Throws.InstanceOf<LedgerException>());

        ledger.Add(Entry("H0001", RunStatus.Passed));
        LedgerRules.CheckCanRun(ledger, "H0001", 1, "holdout", parity: false);
        ledger.Add(Entry("H0001", RunStatus.InsufficientData, "holdout"));
        LedgerRules.CheckCanRun(ledger, "H0001", 1, "holdout", parity: false);
        ledger.Add(Entry("H0001", RunStatus.Passed, "holdout"));

        Assert.That(() => LedgerRules.CheckCanRun(ledger, "H0001", 1, "holdout", parity: false), Throws.InstanceOf<LedgerException>());
    }

    [Test]
    public void The_holdout_is_refused_for_a_version_that_did_not_pass_exploration()
    {
        List<LedgerEntry> ledger = [Entry("H0001", RunStatus.Passed)];

        LedgerRules.CheckCanRun(ledger, "H0001", 1, "holdout", parity: false);
        Assert.That(() => LedgerRules.CheckCanRun(ledger, "H0001", 2, "holdout", parity: false), Throws.InstanceOf<LedgerException>()
            .With.Message.Contains("version 2 passed exploration"));
    }

    [Test]
    public void Failed_cancelled_and_parity_runs_are_not_attempts()
    {
        List<LedgerEntry> ledger =
        [
            Entry("H0001", RunStatus.Rejected),
            Entry("H0002", RunStatus.Failed),
            Entry("H0003", RunStatus.Parity),
            Entry("H0004", RunStatus.Cancelled),
            Entry("H0005", RunStatus.InsufficientData),
            Entry("H0006", RunStatus.Passed, "holdout")
        ];

        Assert.That(LedgerRules.Attempts(ledger, "H0007", 1, "exploration"), Is.EqualTo(2));
        Assert.That(LedgerRules.Attempts(ledger, "H0001", 1, "exploration"), Is.EqualTo(1));
    }

    [Test]
    public void Two_versions_of_one_hypothesis_are_two_attempts()
    {
        List<LedgerEntry> ledger = [Entry("H0001", RunStatus.Rejected)];

        Assert.That(LedgerRules.Attempts(ledger, "H0001", 2, "exploration"), Is.EqualTo(2));
    }

    [Test]
    public void Ten_attempts_count_nine_versions_and_the_current_one()
    {
        List<LedgerEntry> ledger = [.. Enumerable.Range(1, 9).Select(number => Entry($"H000{number}", RunStatus.Rejected))];

        Assert.That(LedgerRules.Attempts(ledger, "H0010", 1, "exploration"), Is.EqualTo(10));
    }

    [Test]
    public void A_correction_takes_a_run_out_of_the_rules()
    {
        LedgerEntry wrong = Entry("H0001", RunStatus.Rejected);
        List<LedgerEntry> ledger =
        [
            wrong,
            Entry("H0001", RunStatus.Failed) with { Corrects = wrong.RunId, Reason = "the funding node priced payments wrong" }
        ];

        LedgerRules.CheckCanRun(ledger, "H0001", 1, "exploration", parity: false);
        Assert.That(LedgerRules.Attempts(ledger, "H0002", 1, "exploration"), Is.EqualTo(1));
    }

    [Test]
    public void A_parity_run_is_always_allowed()
    {
        List<LedgerEntry> ledger = [Entry("H0001", RunStatus.Rejected)];

        LedgerRules.CheckCanRun(ledger, "H0001", 1, "exploration", parity: true);
    }

    private static LedgerEntry Entry(string hypothesis, string status, string period = "exploration", int version = 1)
    {
        return new LedgerEntry
        {
            RunId = Guid.NewGuid().ToString("N"),
            Time = DateTimeOffset.UtcNow,
            Hypothesis = hypothesis,
            Hash = Hash,
            Version = version,
            EngineVersion = 1,
            Period = period,
            Status = status
        };
    }
}
