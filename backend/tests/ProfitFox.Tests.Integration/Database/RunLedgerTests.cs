using Dapper;

using Npgsql;

using ProfitFox.Application.Runs;
using ProfitFox.Domain.Ledger;
using ProfitFox.Infrastructure.Ledger;

namespace ProfitFox.Tests.Integration.Database;

/// <summary>
///     Spec hypothesis-bench «Журнал прогонов», «Нужный t» and «Окончательность вердикта» on the database: the lines in the
///     order they were written, a line and its report written together, and decisions that go one at a time.
/// </summary>
public sealed class RunLedgerTests
{
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private readonly RunLedger _ledger = new RunLedger(BenchDatabase.DataSource);

    [SetUp]
    public async Task Empty()
    {
        await BenchDatabase.Reset();
    }

    [Test]
    public async Task Lines_are_read_back_in_the_order_they_were_written_with_all_their_numbers()
    {
        await Write(Line("H0002", RunStatus.Rejected));
        await Write(Line("H0001", RunStatus.InsufficientData) with { Power = 0.7375618235060951 });

        IReadOnlyList<LedgerEntry> entries = await _ledger.Entries(CancellationToken.None);

        Assert.That(entries.Select(entry => (entry.Hypothesis, entry.Status)), Is.EqualTo(new[] { ("H0002", RunStatus.Rejected), ("H0001", RunStatus.InsufficientData) }));
        Assert.That(entries[1].Power, Is.EqualTo(0.7375618235060951));
        Assert.That((entries[1].Version, entries[1].EngineVersion), Is.EqualTo((1, 1)));
        Assert.That(entries[0].Time, Is.EqualTo(Time).Within(TimeSpan.FromMilliseconds(1)));
    }

    [Test]
    public async Task A_line_and_its_report_appear_together()
    {
        LedgerEntry entry = Line("H0001", RunStatus.Passed);

        LedgerEntry written = await _ledger.Decide(_ => new Decision(entry, "# report"), CancellationToken.None);

        Assert.That(written.RunId, Is.EqualTo(entry.RunId));
        Assert.That(await ReportOf(entry.RunId), Is.EqualTo("# report"));
        Assert.That(await _ledger.Entries(CancellationToken.None), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task A_decision_that_throws_writes_nothing()
    {
        await Write(Line("H0001", RunStatus.Rejected));

        Assert.That(() => _ledger.Decide(entries => LedgerRules.Attempts(entries, "H0001", 1, "exploration") > 0 ? throw new LedgerException("refused") : new Decision(Line("H0001", RunStatus.Passed), "report"), CancellationToken.None),
            Throws.InstanceOf<LedgerException>());

        Assert.That(await _ledger.Entries(CancellationToken.None), Has.Count.EqualTo(1));
        Assert.That(await Count("reports"), Is.Zero);
    }

    [Test]
    public async Task A_line_that_cannot_be_written_takes_its_report_back_with_it()
    {
        LedgerEntry entry = Line("H0001", RunStatus.Passed);
        await Write(entry);

        Assert.That(() => _ledger.Decide(_ => new Decision(entry, "report"), CancellationToken.None), Throws.InstanceOf<PostgresException>());

        Assert.That(await Count("reports"), Is.Zero, "the report of a line that was not written is not written");
    }

    [Test]
    public async Task Two_runs_that_end_together_see_each_other_as_attempts_1_and_2()
    {
        Task<LedgerEntry> first = Verdict("H0001");
        Task<LedgerEntry> second = Verdict("H0002");

        LedgerEntry[] written = await Task.WhenAll(first, second);

        Assert.That(written.Select(entry => entry.Attempts).Order(), Is.EqualTo(new int?[] { 1, 2 }));
        Assert.That((await _ledger.Entries(CancellationToken.None)).Select(entry => entry.Attempts), Is.EqualTo(new int?[] { 1, 2 }), "in the order they were written");
    }

    [Test]
    public async Task The_tenth_version_with_a_verdict_is_the_tenth_attempt()
    {
        foreach (int number in Enumerable.Range(1, 9))
        {
            await Write(Line($"H000{number}", RunStatus.Rejected));
        }

        LedgerEntry tenth = await Verdict("H0010");

        Assert.That(tenth.Attempts, Is.EqualTo(10));
    }

    [Test]
    public async Task A_version_that_was_rejected_cannot_run_again_but_the_next_version_can_as_the_second_attempt()
    {
        await Write(Line("H0001", RunStatus.Rejected));

        Assert.That(() => Verdict("H0001"), Throws.InstanceOf<LedgerException>().With.Message.Contains("already has the verdict rejected"));
        LedgerEntry next = await Verdict("H0001", version: 2);

        Assert.That((next.Version, next.Attempts), Is.EqualTo((2, 2)));
    }

    private static readonly DateTimeOffset Time = new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);

    // A decision of a run: the attempts are counted from the ledger as it is under the lock, and the line is written a moment
    // later, so a run that ended together with this one is still waiting for the lock.
    private Task<LedgerEntry> Verdict(string hypothesis, int version = 1)
    {
        return Task.Run(() => _ledger.Decide(
            entries =>
            {
                LedgerRules.CheckCanRun(entries, hypothesis, version, "exploration", parity: false);
                int attempts = LedgerRules.Attempts(entries, hypothesis, version, "exploration");
                Thread.Sleep(300);
                return new Decision(Line(hypothesis, RunStatus.Rejected) with { Version = version, Attempts = attempts }, "report");
            },
            CancellationToken.None));
    }

    private Task Write(LedgerEntry entry)
    {
        return _ledger.Decide(_ => new Decision(entry, Report: null), CancellationToken.None);
    }

    private static LedgerEntry Line(string hypothesis, string status)
    {
        return new LedgerEntry
        {
            RunId = Guid.NewGuid().ToString("N"),
            Time = Time,
            Hypothesis = hypothesis,
            Hash = Hash,
            Version = 1,
            EngineVersion = 1,
            Period = "exploration",
            Status = status
        };
    }

    private static async Task<string?> ReportOf(string runId)
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<string>("SELECT text FROM reports WHERE run_id = @RunId", new { RunId = runId });
    }

    private static async Task<int> Count(string table)
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>($"SELECT count(*) FROM {table}");
    }
}
