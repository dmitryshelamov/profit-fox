using Dapper;

using FluentMigrator.Runner;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.Infrastructure.Persistence.Postgres;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Lists;
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

    private static readonly string[] BacktestTables = [.. StrategyTables.Concat(["backtest_set_pairs", "backtest_sets", "backtests"]).Order(StringComparer.Ordinal)];

    private static readonly string[] IdeaTables = ["ideas", "market_pairs", "saved_filters", "scanner_lease", "signal_labels", "strategy_ideas"];

    private static readonly string[] IdeaStageTables = [.. BacktestTables.Concat(IdeaTables).Order(StringComparer.Ordinal)];

    private static readonly string[] ScoreTables = ["label_weights"];

    private static readonly string[] Tables = [.. BacktestTables.Concat(IdeaTables).Concat(ScoreTables).Order(StringComparer.Ordinal)];

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
        Assert.That(versions, Is.EqualTo(new[] { 202611200001L, 202611210001L, 202611220001L, 202611220002L, 202611230001L, 202611230002L, 202611230003L, 202611230004L, 202611250001L, 202611250002L, 202611270001L, 202611280001L }));
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
        Assert.That(ProfitFox.Infrastructure.Persistence.Postgres.BenchSchema.Expected, Is.GreaterThanOrEqualTo(202611220002L));
    }

    /// <summary>
    ///     Change add-ideas-and-market, task 6.1: the tables of ideas come on the database of the backtests, keep its tables as
    ///     they were, and go back with Down; the schema a Worker waits for is theirs.
    /// </summary>
    [Test]
    public async Task The_idea_tables_come_and_go_back()
    {
        string connectionString = await BenchDatabase.NewDatabase();
        await using (ServiceProvider services = BenchDatabase.Migrated(connectionString))
        {
            using IServiceScope scope = services.CreateScope();
            scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateDown(202611220002);
        }

        await using var connection = new NpgsqlConnection(connectionString);
        string[] before = await ColumnsOf(connection, BacktestTables);
        Assert.That(await TablesOf(connectionString), Is.EqualTo(BacktestTables));

        await BenchDatabase.Migrated(connectionString).DisposeAsync();

        Assert.That(await TablesOf(connectionString), Is.EqualTo(Tables));
        Assert.That(await ColumnsOf(connection, BacktestTables), Is.EqualTo(before));
        Assert.That(
            await connection.QueryAsync<string>("SELECT indexname FROM pg_indexes WHERE tablename = 'ideas' ORDER BY indexname"),
            Is.EqualTo(new[] { "ideas_birth_ux", "ideas_labels_gin", "ideas_pk", "ideas_state_exit_time_ix", "ideas_state_moment_ix", "ideas_state_score_ix", "ideas_strategy_timeframe_ix", "ideas_symbol_moment_ix" }));
        Assert.That(ProfitFox.Infrastructure.Persistence.Postgres.BenchSchema.Expected, Is.GreaterThanOrEqualTo(202611230001L));
    }

    /// <summary>
    ///     Change add-ideas-and-market, task 18.3: the lease of the scanner gets the failed warm-ups, and Down takes only them
    ///     away.
    /// </summary>
    [Test]
    public async Task The_failures_of_the_scanner_come_on_its_lease_and_go_back()
    {
        string connectionString = await BenchDatabase.NewDatabase();
        await using var connection = new NpgsqlConnection(connectionString);
        const string columns = "SELECT column_name FROM information_schema.columns WHERE table_name = 'scanner_lease' ORDER BY ordinal_position";
        await using (ServiceProvider services = BenchDatabase.Migrated(connectionString))
        {
            Assert.That(
                await connection.QueryAsync<string>(columns),
                Is.EqualTo(new[] { "name", "worker", "until", "live_since", "warmed", "pairs", "failed", "failure", "warmup_left_seconds" }));
            using IServiceScope scope = services.CreateScope();
            scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateDown(202611230001);
        }

        Assert.That(await connection.QueryAsync<string>(columns), Is.EqualTo(new[] { "name", "worker", "until", "live_since", "warmed", "pairs" }));
        Assert.That(await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM pg_indexes WHERE indexname = 'ideas_state_exit_time_ix'"), Is.Zero, "task 18.16: Down takes the index away");
        Assert.That(await TablesOf(connectionString), Is.EqualTo(IdeaStageTables));
    }

    /// <summary>
    ///     Change add-ideas-and-market, task 19.6: the index of the journal has the missing times of exit last, as the journal
    ///     orders them; Down gives back the index of task 18.16.
    /// </summary>
    [Test]
    public async Task The_index_of_the_journal_puts_the_missing_exit_last_and_goes_back()
    {
        string connectionString = await BenchDatabase.NewDatabase();
        await using var connection = new NpgsqlConnection(connectionString);
        const string index = "SELECT indexdef FROM pg_indexes WHERE indexname = 'ideas_state_exit_time_ix'";
        await using (ServiceProvider services = BenchDatabase.Migrated(connectionString))
        {
            Assert.That(await connection.ExecuteScalarAsync<string>(index), Does.EndWith("(state, exit_time DESC NULLS LAST, moment DESC, id)"));
            using IServiceScope scope = services.CreateScope();
            scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateDown(202611230003);
        }

        Assert.That(await connection.ExecuteScalarAsync<string>(index), Does.EndWith("(state, exit_time DESC)"));
    }

    /// <summary>
    ///     Change add-instrument-kind-filter, task 3.1: the kind of a pair of the snapshot comes on the rows already there as
    ///     crypto, a kind out of the list is refused, and Down takes only the column away.
    /// </summary>
    [Test]
    public async Task The_kind_of_a_pair_comes_on_the_snapshot_as_crypto_refuses_another_value_and_goes_back()
    {
        string connectionString = await BenchDatabase.NewDatabase();
        await using var connection = new NpgsqlConnection(connectionString);
        const string columns = "SELECT column_name FROM information_schema.columns WHERE table_name = 'market_pairs' ORDER BY ordinal_position";
        var registrations = new ServiceCollection();
        PostgresRegistrar.Configure(BenchDatabase.Settings(connectionString), registrations);
        await using ServiceProvider services = registrations.BuildServiceProvider();
        using IServiceScope scope = services.CreateScope();
        IMigrationRunner runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
        runner.MigrateUp(202611230004);
        await connection.ExecuteAsync("INSERT INTO market_pairs (symbol, price, updated_at) VALUES ('BTCUSDT', 97000, now()), ('TSLAUSDT', 412.5, now())");

        runner.MigrateUp(202611250001);

        Assert.That(await connection.QueryAsync<(string, string)>("SELECT symbol, kind FROM market_pairs ORDER BY symbol"),
            Is.EqualTo(new[] { ("BTCUSDT", "crypto"), ("TSLAUSDT", "crypto") }));
        await connection.ExecuteAsync("UPDATE market_pairs SET kind = 'stock' WHERE symbol = 'TSLAUSDT'");
        Assert.That(await connection.ExecuteScalarAsync<string>("SELECT kind FROM market_pairs WHERE symbol = 'TSLAUSDT'"), Is.EqualTo("stock"));
        PostgresException? refused = Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync("UPDATE market_pairs SET kind = 'spot' WHERE symbol = 'BTCUSDT'"));
        Assert.That(refused!.SqlState, Is.EqualTo(PostgresErrorCodes.CheckViolation));
        Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync("INSERT INTO market_pairs (symbol, price, updated_at, kind) VALUES ('ETHUSDT', 3500, now(), NULL)"));

        runner.MigrateDown(202611230004);

        Assert.That(await connection.QueryAsync<string>(columns), Does.Not.Contain("kind"));
        Assert.That(await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM market_pairs"), Is.EqualTo(2));
    }

    /// <summary>
    ///     Change add-idea-score, task 2.1 (design.md, decision 1): the weights of the labels are a table by the text of a label
    ///     with two weights of −3 to +3; a weight of 4 is refused by the database, and Down takes only the table away.
    /// </summary>
    [Test]
    public async Task The_weights_of_the_labels_come_refuse_a_weight_out_of_range_and_go_back()
    {
        string connectionString = await BenchDatabase.NewDatabase();
        await using var connection = new NpgsqlConnection(connectionString);
        var registrations = new ServiceCollection();
        PostgresRegistrar.Configure(BenchDatabase.Settings(connectionString), registrations);
        await using ServiceProvider services = registrations.BuildServiceProvider();
        using IServiceScope scope = services.CreateScope();
        IMigrationRunner runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
        runner.MigrateUp(202611270001);

        await connection.ExecuteAsync("INSERT INTO label_weights (text, long_weight, short_weight) VALUES ('↓ BTC 4h', -3, 3)");
        Assert.That(await connection.QuerySingleAsync<(short, short)>("SELECT long_weight, short_weight FROM label_weights WHERE text = '↓ BTC 4h'"), Is.EqualTo(((short)-3, (short)3)));
        PostgresException? tooHigh = Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync("INSERT INTO label_weights (text, long_weight, short_weight) VALUES ('фандинг ↑', 4, 0)"));
        Assert.That(tooHigh!.SqlState, Is.EqualTo(PostgresErrorCodes.CheckViolation));
        PostgresException? tooLow = Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync("UPDATE label_weights SET short_weight = -4 WHERE text = '↓ BTC 4h'"));
        Assert.That(tooLow!.SqlState, Is.EqualTo(PostgresErrorCodes.CheckViolation));
        PostgresException? twice = Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync("INSERT INTO label_weights (text, long_weight, short_weight) VALUES ('↓ BTC 4h', 0, 0)"));
        Assert.That(twice!.SqlState, Is.EqualTo(PostgresErrorCodes.UniqueViolation));

        runner.MigrateDown(202611250002);

        Assert.That(await TablesOf(connectionString), Does.Not.Contain("label_weights"));
        Assert.That(await TablesOf(connectionString), Does.Contain("ideas"));
    }

    /// <summary>
    ///     Change add-idea-score, task 8.1 (design.md, decision 2): the score of an idea comes as a column filled by
    ///     <c>ScoreOf</c> on the ideas and weights already there, with the index of the labels and that of the lists by score;
    ///     Down takes only them away.
    /// </summary>
    [Test]
    public async Task The_score_of_an_idea_comes_filled_from_the_weights_already_there_and_goes_back()
    {
        string connectionString = await BenchDatabase.NewDatabase();
        await using var connection = new NpgsqlConnection(connectionString);
        var registrations = new ServiceCollection();
        PostgresRegistrar.Configure(BenchDatabase.Settings(connectionString), registrations);
        await using ServiceProvider services = registrations.BuildServiceProvider();
        using IServiceScope scope = services.CreateScope();
        IMigrationRunner runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
        runner.MigrateUp(202611270001);
        await connection.ExecuteAsync("INSERT INTO label_weights (text, long_weight, short_weight) VALUES ('↓ BTC 4h', -2, 1), ('оборот ↑ 1h', 1, 0), ('⚡ всплеск', 0, -1)");
        await connection.ExecuteAsync(
            """
            INSERT INTO ideas (id, strategy_id, strategy_name, strategy_version, timeframe, symbol, side, moment, size, after_minutes, until, candle_close, labels, state)
            SELECT gen_random_uuid(), gen_random_uuid(), 's', 1, '1h', v.symbol, v.side, now(), 100, 0, now() + interval '1 day', 1, v.labels, v.state
            FROM (VALUES ('AUSDT', 'long', ARRAY['↓ BTC 4h', 'оборот ↑ 1h', '⚡ всплеск'], 'open'),
                         ('BUSDT', 'short', ARRAY['↓ BTC 4h', 'оборот ↑ 1h', '⚡ всплеск'], 'closed'),
                         ('CUSDT', 'long', ARRAY[]::text[], 'open'),
                         ('DUSDT', 'short', ARRAY['без веса', '⚡ всплеск'], 'closed'),
                         ('EUSDT', 'long', ARRAY['↓ BTC 4h'], 'excluded')) AS v(symbol, side, labels, state)
            """);

        runner.MigrateUp(202611280001);

        string scoreOf = ListColumns.ScoreOf("i");
        Assert.That(
            await connection.QueryAsync<(string, int, int)>($"SELECT i.symbol, i.score::int, {scoreOf} FROM ideas i ORDER BY i.symbol"),
            Is.EqualTo(new[] { ("AUSDT", -1, -1), ("BUSDT", 0, 0), ("CUSDT", 0, 0), ("DUSDT", -1, -1), ("EUSDT", -2, -2) }));
        Assert.That(
            await connection.QueryAsync<string>("SELECT indexdef FROM pg_indexes WHERE tablename = 'ideas' AND indexname IN ('ideas_labels_gin', 'ideas_state_score_ix') ORDER BY indexname"),
            Is.EqualTo(new[]
            {
                "CREATE INDEX ideas_labels_gin ON public.ideas USING gin (labels)",
                "CREATE INDEX ideas_state_score_ix ON public.ideas USING btree (state, score DESC NULLS LAST, moment DESC, id)"
            }));
        Assert.That(await ColumnsOf(connection, ["ideas"]), Does.Contain("ideas.score:smallint:NO"));

        runner.MigrateDown(202611270001);

        Assert.That(await ColumnsOf(connection, ["ideas"]), Has.None.StartsWith("ideas.score:"));
        Assert.That(await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM pg_indexes WHERE tablename = 'ideas' AND indexname IN ('ideas_labels_gin', 'ideas_state_score_ix')"), Is.Zero);
        Assert.That(await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM ideas"), Is.EqualTo(5));
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
