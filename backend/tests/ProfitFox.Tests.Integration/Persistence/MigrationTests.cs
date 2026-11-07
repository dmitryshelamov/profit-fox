using Dapper;

using Npgsql;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

public class MigrationTests
{
    [Test]
    public async Task Candles_table_is_a_hypertable()
    {
        await using NpgsqlDataSource dataSource = MarketDataDatabase.Open();
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();

        IEnumerable<string> hypertables = await connection.QueryAsync<string>("SELECT hypertable_name FROM timescaledb_information.hypertables");

        hypertables.ShouldBe(["candles", "open_interest", "idea_cases", "liquidations"], ignoreOrder: true);
    }

    // Jobs created before the column existed must read as never interrupted; the column is now the count of lost leases.
    [Test]
    public async Task Import_jobs_count_lost_leases_from_zero()
    {
        await using NpgsqlDataSource dataSource = MarketDataDatabase.Open();
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();

        (string DataType, string IsNullable, string Default) column = await connection.QuerySingleAsync<(string, string, string)>(
            """
            SELECT data_type, is_nullable, column_default
            FROM information_schema.columns
            WHERE table_name = 'candle_import_jobs' AND column_name = 'lost_leases'
            """);

        column.ShouldBe(("integer", "NO", "0"));
    }

    [Test]
    public async Task Strategy_library_starts_at_revision_zero_without_graphs()
    {
        await using NpgsqlDataSource dataSource = MarketDataDatabase.Open();
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();

        (await connection.QueryAsync<long>("SELECT revision FROM strategy_library")).ShouldBe([0L]);
        (await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM strategies")).ShouldBe(0L);
        (await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM strategy_composite_nodes")).ShouldBe(0L);
    }

    // Task 3.1: the tables of the ideas, and the setups of a snapshot row empty until the scanner writes them.
    [Test]
    public async Task Ideas_have_their_tables_and_snapshot_rows_have_no_setups_yet()
    {
        await using NpgsqlDataSource dataSource = MarketDataDatabase.Open();
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();

        IEnumerable<string> tables = await connection.QueryAsync<string>(
            "SELECT table_name FROM information_schema.tables WHERE table_name IN ('ideas', 'idea_cases', 'idea_buckets', 'idea_replay', 'idea_volatility', 'saved_filters')");
        (string DataType, string IsNullable, string Default) setups = await connection.QuerySingleAsync<(string, string, string)>(
            """
            SELECT data_type, is_nullable, column_default
            FROM information_schema.columns
            WHERE table_name = 'market_snapshot' AND column_name = 'setups'
            """);

        tables.Count().ShouldBe(6);
        setups.ShouldBe(("jsonb", "NO", "'{}'::jsonb"));
    }

    // Decision 3: the buckets keep the counts of their outcomes and the interval of the expectancy, and no baseline.
    [Test]
    public async Task Buckets_have_their_outcomes_and_the_interval_of_the_expectancy_without_a_baseline()
    {
        await using NpgsqlDataSource dataSource = MarketDataDatabase.Open();
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();

        IEnumerable<(string Name, string DataType, string IsNullable)> columns = await connection.QueryAsync<(string, string, string)>(
            """
            SELECT column_name, data_type, is_nullable
            FROM information_schema.columns
            WHERE table_name = 'idea_buckets' AND column_name IN ('targets', 'stops', 'expectancy_low', 'expectancy_high', 'baseline')
            """);

        columns.ShouldBe(
        [
            ("targets", "integer", "NO"),
            ("stops", "integer", "NO"),
            ("expectancy_low", "numeric", "YES"),
            ("expectancy_high", "numeric", "YES")
        ],
            ignoreOrder: true);
    }

    // Decision 6 of honest-idea-intervals: the own numbers of a bucket beside those of the model, the reports of calibration by scope and the
    // start of a version of the rules; the image before it writes its buckets and reports without the new columns.
    [Test]
    public async Task Buckets_keep_their_own_numbers_calibration_has_a_scope_and_versions_have_a_start()
    {
        await using NpgsqlDataSource dataSource = MarketDataDatabase.Open();
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();

        IEnumerable<(string Table, string Name, string DataType, string IsNullable, string? Default)> columns =
            await connection.QueryAsync<(string, string, string, string, string?)>(
                """
                SELECT table_name, column_name, data_type, is_nullable, column_default
                FROM information_schema.columns
                WHERE (table_name = 'idea_buckets' AND (column_name LIKE 'own_%' OR column_name IN ('parent_weight', 'few_own_cases')))
                    OR (table_name = 'idea_calibration' AND column_name = 'scope') OR table_name = 'idea_engine_versions'
                """);
        IEnumerable<string> calibrationKey = await connection.QueryAsync<string>(
            """
            SELECT a.attname FROM pg_index i JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey)
            WHERE i.indrelid = 'idea_calibration'::regclass AND i.indisprimary
            """);
        string versionKey = await connection.QuerySingleAsync<string>(
            """
            SELECT a.attname FROM pg_index i JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey)
            WHERE i.indrelid = 'idea_engine_versions'::regclass AND i.indisprimary
            """);
        await connection.ExecuteAsync(
            """
            INSERT INTO idea_buckets (engine_version, days, setup, type, "interval", side, category, cases, targets, stops, expired, probability,
                probability_low, probability_high, expectancy, expectancy_low, expectancy_high, signals)
            VALUES (5, 90, 'impulse', 'two_r', '15', 'long', 'linear', 12, 6, 6, 0, NULL, NULL, NULL, 0.5, NULL, NULL, NULL);
            INSERT INTO idea_calibration (engine_version, days, category, computed_at, report) VALUES (5, 90, 'all', now(), '{}');
            """);

        columns.ShouldBe(
        [
            ("idea_buckets", "own_probability", "numeric", "YES", null),
            ("idea_buckets", "own_probability_low", "numeric", "YES", null),
            ("idea_buckets", "own_probability_high", "numeric", "YES", null),
            ("idea_buckets", "own_expectancy", "numeric", "NO", "0"),
            ("idea_buckets", "own_expectancy_low", "numeric", "YES", null),
            ("idea_buckets", "own_expectancy_high", "numeric", "YES", null),
            ("idea_buckets", "parent_weight", "numeric", "YES", null),
            ("idea_buckets", "few_own_cases", "boolean", "NO", "false"),
            ("idea_calibration", "scope", "text", "NO", "'all'::text"),
            ("idea_engine_versions", "engine_version", "integer", "NO", null),
            ("idea_engine_versions", "started_at", "timestamp with time zone", "NO", null)
        ],
            ignoreOrder: true);
        calibrationKey.ShouldBe(["engine_version", "days", "category", "scope"], ignoreOrder: true);
        versionKey.ShouldBe("engine_version");
        (await connection.QuerySingleAsync<string>("SELECT scope FROM idea_calibration")).ShouldBe("all");
    }

    // Liquidations are kept for good: daily chunks, one row per event, compressed by pair after 7 days by a policy of TimescaleDB.
    [Test]
    public async Task Liquidations_are_daily_chunks_unique_by_event_and_compressed_after_a_week()
    {
        await using NpgsqlDataSource dataSource = MarketDataDatabase.Open();
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();

        TimeSpan chunk = await connection.QuerySingleAsync<TimeSpan>(
            "SELECT time_interval FROM timescaledb_information.dimensions WHERE hypertable_name = 'liquidations'");
        string index = await connection.QuerySingleAsync<string>(
            "SELECT indexdef FROM pg_indexes WHERE tablename = 'liquidations' AND indexname = 'UX_liquidations_event'");
        IEnumerable<(string Column, bool SegmentBy)> compression = await connection.QueryAsync<(string, bool)>(
            "SELECT attname, segmentby_column_index IS NOT NULL FROM timescaledb_information.compression_settings WHERE hypertable_name = 'liquidations'");
        string? compressAfter = await connection.QuerySingleOrDefaultAsync<string>(
            """
            SELECT config ->> 'compress_after' FROM timescaledb_information.jobs
            WHERE hypertable_name = 'liquidations' AND proc_name = 'policy_compression'
            """);

        chunk.ShouldBe(TimeSpan.FromDays(1));
        index.ShouldBe("""CREATE UNIQUE INDEX "UX_liquidations_event" ON public.liquidations USING btree (category, symbol, "time", side, price, quantity)""");
        compression.ShouldBe([("category", true), ("symbol", true), ("time", false)], ignoreOrder: true);
        compressAfter.ShouldBe("7 days");
    }

    [Test]
    public async Task Stream_breaks_are_keyed_by_stream_and_the_time_it_was_lost()
    {
        await using NpgsqlDataSource dataSource = MarketDataDatabase.Open();
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();

        IEnumerable<(string Name, string DataType, string IsNullable)> columns = await connection.QueryAsync<(string, string, string)>(
            "SELECT column_name, data_type, is_nullable FROM information_schema.columns WHERE table_name = 'stream_breaks'");
        IEnumerable<string> key = await connection.QueryAsync<string>(
            """
            SELECT a.attname FROM pg_index i JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey)
            WHERE i.indrelid = 'stream_breaks'::regclass AND i.indisprimary
            """);

        columns.ShouldBe(
            [("stream", "text", "NO"), ("lost_at", "timestamp with time zone", "NO"), ("restored_at", "timestamp with time zone", "NO")],
            ignoreOrder: true);
        key.ShouldBe(["stream", "lost_at"], ignoreOrder: true);
    }
}