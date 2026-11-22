using Dapper;

using FluentMigrator.Runner;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator;

namespace ProfitFox.Tests.Integration.Infrastructure.Persistence.Postgres.FluentMigrator;

/// <summary>
///     The migrations of the bench database (design add-bench-service, decision 2; change add-strategy-constructor, task 3.1)
///     and what they make the database refuse: a change of the ledger or of a report, and a second waiting run of one
///     version, period and kind.
/// </summary>
public sealed class MigrationTests
{
    private static readonly string[] BenchTables = ["hypothesis_versions", "ledger", "reports", "runs", "signal_versions", "signals"];

    private static readonly string[] StrategyTables = [.. BenchTables.Concat(["strategies", "strategy_signals", "strategy_versions"]).Order(StringComparer.Ordinal)];

    private static readonly string[] Tables = [.. StrategyTables.Concat(["backtest_set_pairs", "backtest_sets", "backtests"]).Order(StringComparer.Ordinal)];

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

        Assert.That(await TablesOf(connectionString), Is.EqualTo(Tables));
    }

    [Test]
    public async Task After_the_start_the_strategy_tables_are_there_and_the_database_has_every_migration()
    {
        string connectionString = await BenchDatabase.NewDatabase();

        await BenchDatabase.Migrated(connectionString).DisposeAsync();

        await using var connection = new NpgsqlConnection(connectionString);
        long[] versions = [.. await connection.QueryAsync<long>("SELECT \"Version\" FROM \"VersionInfo\" ORDER BY \"Version\"")];
        Assert.That(versions, Is.EqualTo(new[] { 202611200001L, 202611210001L, 202611220001L, 202611220002L }));
        Assert.That(await TablesOf(connectionString), Is.SupersetOf(new[] { "strategies", "strategy_versions", "strategy_signals" }));
    }

    [Test]
    public async Task Down_of_the_strategy_migration_removes_only_its_tables()
    {
        string connectionString = await BenchDatabase.NewDatabase();

        await using (ServiceProvider services = BenchDatabase.Migrated(connectionString))
        {
            using IServiceScope scope = services.CreateScope();
            scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateDown(202611200001);
        }

        Assert.That(await TablesOf(connectionString), Is.EqualTo(BenchTables));
    }

    [Test]
    public async Task The_kind_of_a_strategy_is_a_column_that_may_be_empty_and_down_removes_only_it()
    {
        string connectionString = await BenchDatabase.NewDatabase();

        await using (ServiceProvider services = BenchDatabase.Migrated(connectionString))
        {
            await using var connection = new NpgsqlConnection(connectionString);
            string nullable = await connection.ExecuteScalarAsync<string>(
                "SELECT is_nullable FROM information_schema.columns WHERE table_name = 'strategies' AND column_name = 'kind'") ?? "";
            Assert.That(nullable, Is.EqualTo("YES"), "a strategy saved before kinds keeps an empty kind");

            using IServiceScope scope = services.CreateScope();
            scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateDown(202611210001);
        }

        await using var after = new NpgsqlConnection(connectionString);
        Assert.That(await after.ExecuteScalarAsync<int>("SELECT count(*) FROM information_schema.columns WHERE table_name = 'strategies' AND column_name = 'kind'"), Is.Zero);
        Assert.That(await TablesOf(connectionString), Is.EqualTo(StrategyTables));
    }

    /// <summary>
    ///     Change add-strategy-backtest, task 6.1: the tables of backtests on a database of the version before them, which keeps
    ///     its rows and the columns of its tables, and the schema a Worker waits for is the new one.
    /// </summary>
    [Test]
    public async Task The_backtest_tables_come_on_the_former_database_and_leave_its_tables_as_they_were()
    {
        string connectionString = await BenchDatabase.NewDatabase();
        await using (ServiceProvider services = BenchDatabase.Migrated(connectionString))
        {
            using IServiceScope scope = services.CreateScope();
            scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateDown(202611220001);
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.ExecuteAsync("INSERT INTO hypothesis_versions (hypothesis_id, version, hash, text) VALUES ('H0001', 1, 'hash', '{}')");
        await connection.ExecuteAsync("INSERT INTO runs (id, hypothesis_id, version, period, kind, status) VALUES ('a', 'H0001', 1, 'exploration', 'verdict', 'finished')");
        string[] before = await ColumnsOf(connection, StrategyTables);
        Assert.That(await TablesOf(connectionString), Is.EqualTo(StrategyTables));

        await BenchDatabase.Migrated(connectionString).DisposeAsync();

        Assert.That(await TablesOf(connectionString), Is.EqualTo(Tables));
        Assert.That(await ColumnsOf(connection, StrategyTables), Is.EqualTo(before), "the former tables keep their columns");
        Assert.That(await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM runs"), Is.EqualTo(1), "and their rows");
        Assert.That(ProfitFox.Infrastructure.Persistence.Postgres.BenchSchema.Expected, Is.EqualTo(202611220002L));
    }

    private static async Task<string[]> ColumnsOf(NpgsqlConnection connection, string[] tables)
    {
        return [.. await connection.QueryAsync<string>(
            "SELECT table_name || '.' || column_name || ':' || data_type || ':' || is_nullable FROM information_schema.columns WHERE table_schema = 'public' AND table_name = ANY(@Tables) ORDER BY table_name, ordinal_position",
            new { Tables = tables })];
    }

    [Test]
    public async Task A_version_of_a_strategy_keeps_the_version_of_the_signal_it_points_at()
    {
        await Execute("INSERT INTO signals (id, name, layout, version) VALUES ('00000000-0000-0000-0000-000000000001', 'funding-high', '{}', 1)");
        await Execute("INSERT INTO signal_versions (signal_id, version, graph) VALUES ('00000000-0000-0000-0000-000000000001', 1, '{}')");
        await Execute("INSERT INTO strategies (id, name, layout, version) VALUES ('00000000-0000-0000-0000-000000000002', 'funding-short', '{}', 1)");
        await Execute("INSERT INTO strategy_versions (strategy_id, version, graph) VALUES ('00000000-0000-0000-0000-000000000002', 1, '{}')");
        await Execute("INSERT INTO strategy_signals (strategy_id, version, node, signal_id, signal_version) VALUES ('00000000-0000-0000-0000-000000000002', 1, 's', '00000000-0000-0000-0000-000000000001', 1)");

        Assert.That(() => Execute("DELETE FROM signals"), Throws.InstanceOf<PostgresException>().With.Property(nameof(PostgresException.SqlState)).EqualTo("23503"));
        Assert.That(
            () => Execute("INSERT INTO strategy_signals (strategy_id, version, node, signal_id, signal_version) VALUES ('00000000-0000-0000-0000-000000000002', 1, 't', '00000000-0000-0000-0000-000000000001', 2)"),
            Throws.InstanceOf<PostgresException>().With.Property(nameof(PostgresException.SqlState)).EqualTo("23503"), "no such version");

        await Execute("DELETE FROM strategies");
        await Execute("DELETE FROM signals");
        Assert.That(await Scalar<int>("SELECT count(*) FROM signal_versions"), Is.Zero, "deleting the strategy lifts the refusal");
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

    private static async Task<string[]> TablesOf(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        return [.. await connection.QueryAsync<string>("SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_name <> 'VersionInfo' ORDER BY table_name")];
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
