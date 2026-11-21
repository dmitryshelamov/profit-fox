using ProfitFox.Domain.Entities.Runs;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Runs;

namespace ProfitFox.Tests.Unit.Domain.Entities.Runs;

/// <summary>
///     Spec bench-run-queue: what a run of the queue tells by its row (design match-market-data-structure, decision 1).
/// </summary>
public sealed class RunTests
{
    [TestCase(RunState.Queued, true)]
    [TestCase(RunState.Running, false)]
    [TestCase(RunState.Finished, false)]
    public void A_run_is_queued_only_while_it_waits(string status, bool queued)
    {
        Assert.That(RunGenerator.Generate(status: status).IsQueued, Is.EqualTo(queued));
    }

    [TestCase(Run.MaxLostLeases - 1, false)]
    [TestCase(Run.MaxLostLeases, true)]
    [TestCase(Run.MaxLostLeases + 1, true)]
    public void A_run_is_interrupted_too_often_from_the_third_lost_lease(int lostLeases, bool interrupted)
    {
        Assert.That(RunGenerator.Generate(status: RunState.Running, lostLeases: lostLeases).InterruptedTooOften, Is.EqualTo(interrupted));
    }

    [TestCase("H0002", 1, "exploration", false)]
    [TestCase("H0001", 2, "exploration", false)]
    [TestCase("H0001", 1, "holdout", false)]
    [TestCase("H0001", 1, "exploration", true)]
    public void A_run_is_not_of_a_version_period_and_kind_that_differ_in_one_field(string hypothesis, int version, string period, bool parity)
    {
        Run run = RunGenerator.Generate(hypothesis: "H0001", version: 1, period: "exploration", parity: false);

        Assert.That(run.IsOf(hypothesis, version, period, parity), Is.False);
    }

    [Test]
    public void A_run_is_of_its_own_version_period_and_kind()
    {
        Run run = RunGenerator.Generate(hypothesis: "H0001", version: 2, period: "holdout", parity: true);

        Assert.That(run.IsOf("H0001", version: 2, "holdout", parity: true), Is.True);
    }

    [Test]
    public void Restore_keeps_every_value_of_the_row()
    {
        var queuedAt = new DateTimeOffset(year: 2026, month: 11, day: 2, hour: 3, minute: 4, second: 5, TimeSpan.Zero);

        var run = Run.Restore(
            "run-7",
            "H0003",
            version: 4,
            "new-data",
            parity: true,
            RunState.Running,
            queuedAt,
            queuedAt.AddMinutes(1),
            finishedAt: null,
            "worker-2",
            queuedAt.AddMinutes(2),
            lostLeases: 1,
            cancelRequested: true,
            pairsDone: 10,
            pairsTotal: 20);

        Assert.Multiple(() =>
        {
            Assert.That(run.Id, Is.EqualTo("run-7"));
            Assert.That(run.Hypothesis, Is.EqualTo("H0003"));
            Assert.That(run.Version, Is.EqualTo(4));
            Assert.That(run.Period, Is.EqualTo("new-data"));
            Assert.That(run.Parity, Is.True);
            Assert.That(run.Status, Is.EqualTo(RunState.Running));
            Assert.That(run.QueuedAt, Is.EqualTo(queuedAt));
            Assert.That(run.StartedAt, Is.EqualTo(queuedAt.AddMinutes(1)));
            Assert.That(run.FinishedAt, Is.Null);
            Assert.That(run.LeaseOwner, Is.EqualTo("worker-2"));
            Assert.That(run.LeasedUntil, Is.EqualTo(queuedAt.AddMinutes(2)));
            Assert.That(run.LostLeases, Is.EqualTo(1));
            Assert.That(run.CancelRequested, Is.True);
            Assert.That(run.PairsDone, Is.EqualTo(10));
            Assert.That(run.PairsTotal, Is.EqualTo(20));
            Assert.That(run.DomainEvents, Is.Empty);
        });
    }
}
