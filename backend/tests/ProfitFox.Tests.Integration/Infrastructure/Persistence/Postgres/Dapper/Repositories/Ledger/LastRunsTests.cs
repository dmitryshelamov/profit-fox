using Dapper;

using Npgsql;

using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Hypotheses;
using ProfitFox.Domain.ValueObjects.Ledger;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ledger;

namespace ProfitFox.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ledger;

/// <summary>
///     Spec bench-run-queue «Последний прогон гипотезы» at the level of the ledger (change redesign-bench-ui, task 2.4): the
///     line with the latest time of each version of a hypothesis, by one read, with the kind of its run.
/// </summary>
public sealed class LastRunsTests
{
    private static readonly DateTimeOffset Time = new DateTimeOffset(2026, 11, 21, 0, 1, 0, TimeSpan.Zero);
    private readonly LedgerRepository _ledger = new LedgerRepository(BenchDatabase.DataSource);

    // A run is of a stored version of a hypothesis, so the versions the tests write lines of are stored first.
    [SetUp]
    public async Task Empty()
    {
        await BenchDatabase.Reset();
        IHypothesisRepository hypotheses = BenchServices.Get<IHypothesisRepository>();
        foreach ((string id, int versions) in new[] { ("H0001", 2), ("H0002", 3) })
        {
            foreach (int version in Enumerable.Range(1, versions))
            {
                await hypotheses.AddAsync(id, $"{id}-{version}".PadRight(64, '0'), $"text {version}", CancellationToken.None);
            }
        }
    }

    [Test]
    public async Task Of_two_lines_of_one_hypothesis_the_later_one_is_given()
    {
        await Write("H0001", version: 1, RunStatus.Failed, Time);
        LedgerEntry later = await Write("H0001", version: 1, RunStatus.InsufficientData, Time.AddHours(2));

        FinishedRun last = (await Last(("H0001", 1))).Single();

        Assert.That((last.Entry.RunId, last.Entry.Status, last.Parity), Is.EqualTo((later.RunId, RunStatus.InsufficientData, false)));
        Assert.That(last.Entry.Time, Is.EqualTo(Time.AddHours(2)).Within(TimeSpan.FromMilliseconds(1)));
    }

    [Test]
    public async Task Of_lines_of_one_time_the_one_written_last_is_given()
    {
        await Write("H0001", version: 1, RunStatus.Failed, Time);
        LedgerEntry second = await Write("H0001", version: 1, RunStatus.Cancelled, Time);

        Assert.That((await Last(("H0001", 1))).Single().Entry.RunId, Is.EqualTo(second.RunId));
    }

    [Test]
    public async Task Each_version_asked_for_has_its_own_line_and_the_others_have_none()
    {
        LedgerEntry first = await Write("H0001", version: 1, RunStatus.Rejected, Time);
        await Write("H0001", version: 2, RunStatus.Passed, Time.AddHours(1));
        LedgerEntry parity = await Write("H0002", version: 3, RunStatus.Parity, Time.AddHours(2));

        IReadOnlyList<FinishedRun> last = await Last(("H0001", 1), ("H0002", 3), ("H0002", 1), ("H0003", 1));

        Assert.That(
            last.Select(run => (run.Entry.Hypothesis, run.Entry.Version, run.Entry.RunId, run.Parity)),
            Is.EquivalentTo(new[] { ("H0001", 1, first.RunId, false), ("H0002", 3, parity.RunId, true) }));
    }

    [Test]
    public async Task No_versions_and_an_empty_ledger_give_nothing()
    {
        Assert.That(await Last(), Is.Empty);
        Assert.That(await Last(("H0001", 1)), Is.Empty);
    }

    private Task<IReadOnlyList<FinishedRun>> Last(params (string Id, int Version)[] versions)
    {
        return _ledger.LastAsync([.. versions.Select(version => new HypothesisVersion(version.Id, version.Version))], CancellationToken.None);
    }

    // A line with the finished row of its run, the way the bench writes them.
    private async Task<LedgerEntry> Write(string hypothesis, int version, string status, DateTimeOffset time)
    {
        var entry = new LedgerEntry
        {
            RunId = Guid.NewGuid().ToString("N"),
            Time = time,
            Hypothesis = hypothesis,
            Hash = new string('a', 64),
            Version = version,
            EngineVersion = 1,
            Period = "exploration",
            Status = status
        };
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO runs (id, hypothesis_id, version, period, kind, status, finished_at) VALUES (@RunId, @Hypothesis, @Version, @Period, @Kind, 'finished', now())",
            new { entry.RunId, entry.Hypothesis, entry.Version, entry.Period, Kind = status == RunStatus.Parity ? "parity" : "verdict" });
        await _ledger.DecideAsync(_ => new Decision(entry, Report: "# report"), CancellationToken.None);
        return entry;
    }
}
