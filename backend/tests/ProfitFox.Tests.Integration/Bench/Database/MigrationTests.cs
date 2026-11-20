using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ProfitFox.Bench.Infrastructure.Persistence;

namespace ProfitFox.Tests.Integration.Bench.Database;

/// <summary>
///     The first migration of the bench database (design add-bench-service, decision 2) and what it makes the database
///     refuse: a change of the ledger or of a report, and a second waiting run of one version, period and kind.
/// </summary>
public sealed class MigrationTests
{
    private static readonly string[] Tables = ["hypothesis_versions", "ledger", "reports", "runs", "signal_versions", "signals"];

    [SetUp]
    public async Task Empty()
    {
        await BenchDatabase.Reset();
    }

    [Test]
    public async Task The_migration_runs_on_an_empty_database_and_a_second_time_changes_nothing()
    {
        string connectionString = await BenchDatabase.NewDatabase();

        await using (ServiceProvider services = BenchDatabase.Migrated(connectionString))
        {
            services.MigrateBench();
        }

        await using var connection = new NpgsqlConnection(connectionString);
        string[] tables = [.. await connection.QueryAsync<string>("SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_name <> 'VersionInfo' ORDER BY table_name")];
        Assert.That(tables, Is.EqualTo(Tables));
    }

    [Test]
    public async Task A_line_of_the_ledger_is_never_changed_or_deleted()
    {
        await AddLine("run-1");

        Assert.That(() => Execute("UPDATE ledger SET status = 'passed' WHERE run_id = 'run-1'"), Throws.InstanceOf<PostgresException>().With.Property(nameof(PostgresException.SqlState)).EqualTo("23001"));
        Assert.That(() => Execute("DELETE FROM ledger WHERE run_id = 'run-1'"), Throws.InstanceOf<PostgresException>().With.Property(nameof(PostgresException.SqlState)).EqualTo("23001"));
        Assert.That(await Scalar<string>("SELECT status FROM ledger WHERE run_id = 'run-1'"), Is.EqualTo("rejected"));
    }

    [Test]
    public async Task A_report_is_never_changed_or_deleted()
    {
        await AddLine("run-1");
        await Execute("INSERT INTO reports (run_id, text) VALUES ('run-1', 'report')");

        Assert.That(() => Execute("UPDATE reports SET text = 'other' WHERE run_id = 'run-1'"), Throws.InstanceOf<PostgresException>().With.Property(nameof(PostgresException.SqlState)).EqualTo("23001"));
        Assert.That(() => Execute("DELETE FROM reports WHERE run_id = 'run-1'"), Throws.InstanceOf<PostgresException>().With.Property(nameof(PostgresException.SqlState)).EqualTo("23001"));
        Assert.That(await Scalar<string>("SELECT text FROM reports WHERE run_id = 'run-1'"), Is.EqualTo("report"));
    }

    [Test]
    public async Task A_second_waiting_run_of_the_same_version_period_and_kind_is_refused_by_the_index()
    {
        await AddHypothesis();
        await Execute("INSERT INTO runs (id, hypothesis_id, version, period, kind, status) VALUES ('a', 'H0001', 1, 'exploration', 'verdict', 'queued')");

        Assert.That(
            () => Execute("INSERT INTO runs (id, hypothesis_id, version, period, kind, status) VALUES ('b', 'H0001', 1, 'exploration', 'verdict', 'queued')"),
            Throws.InstanceOf<PostgresException>().With.Property(nameof(PostgresException.SqlState)).EqualTo("23505"));
        Assert.That(
            () => Execute("INSERT INTO runs (id, hypothesis_id, version, period, kind, status) VALUES ('c', 'H0001', 1, 'exploration', 'verdict', 'running')"),
            Throws.InstanceOf<PostgresException>().With.Property(nameof(PostgresException.SqlState)).EqualTo("23505"), "a running run counts too");
    }

    [Test]
    public async Task Another_kind_period_or_a_finished_run_does_not_stop_a_new_one()
    {
        await AddHypothesis();
        await Execute("INSERT INTO runs (id, hypothesis_id, version, period, kind, status) VALUES ('a', 'H0001', 1, 'exploration', 'verdict', 'queued')");

        await Execute("INSERT INTO runs (id, hypothesis_id, version, period, kind, status) VALUES ('b', 'H0001', 1, 'exploration', 'parity', 'queued')");
        await Execute("INSERT INTO runs (id, hypothesis_id, version, period, kind, status) VALUES ('c', 'H0001', 1, 'holdout', 'verdict', 'queued')");
        await Execute("UPDATE runs SET status = 'finished' WHERE id = 'a'");
        await Execute("INSERT INTO runs (id, hypothesis_id, version, period, kind, status) VALUES ('d', 'H0001', 1, 'exploration', 'verdict', 'queued')");

        Assert.That(await Scalar<int>("SELECT count(*) FROM runs"), Is.EqualTo(4));
    }

    private static Task AddHypothesis()
    {
        return Execute("INSERT INTO hypothesis_versions (hypothesis_id, version, hash, text) VALUES ('H0001', 1, 'hash', '{}')");
    }

    private static Task AddLine(string runId)
    {
        return Execute($"INSERT INTO ledger (run_id, time, hypothesis_id, hash, version, engine_version, period, status) VALUES ('{runId}', now(), 'H0001', 'hash', 1, 1, 'exploration', 'rejected')");
    }

    private static async Task Execute(string sql)
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(sql);
    }

    private static async Task<T?> Scalar<T>(string sql)
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<T>(sql);
    }
}
