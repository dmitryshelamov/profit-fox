using System.Text.Json;

using Dapper;

using Npgsql;

using NpgsqlTypes;

using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Converters;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;

internal sealed class IdeaReplayRepository(NpgsqlDataSource dataSource) : IIdeaReplayRepository
{
    public async Task<IReadOnlyDictionary<Instrument, IdeaReplayCursor>> GetAsync(int engineVersion, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<Cursor> rows = await connection.QueryAsync<Cursor>(
            new CommandDefinition(
                """SELECT category, symbol, replayed_until AS "ReplayedUntil", open_signals::text AS "OpenSignals" FROM idea_replay WHERE engine_version = @Version""",
                new { Version = engineVersion },
                cancellationToken: cancellationToken));

        return rows.ToDictionary(
            row =>
            {
                MarketDataCodes.TryParseCategory(row.Category, out MarketCategory category);

                return new Instrument(category, row.Symbol);
            },
            row => new IdeaReplayCursor(row.ReplayedUntil, [.. JsonSerializer.Deserialize<List<SignalDb>>(row.OpenSignals)!.Select(SignalDbConverter.ToDomain)]));
    }

    public async Task SetAsync(Instrument instrument, int engineVersion, IdeaReplayCursor cursor, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            """
            INSERT INTO idea_replay (category, symbol, engine_version, replayed_until, open_signals) VALUES (@category, @symbol, @version, @until, @open)
            ON CONFLICT (category, symbol, engine_version) DO UPDATE SET replayed_until = EXCLUDED.replayed_until, open_signals = EXCLUDED.open_signals
            """);
        command.Parameters.AddWithValue("category", instrument.Category.ToCode());
        command.Parameters.AddWithValue("symbol", instrument.Symbol);
        command.Parameters.AddWithValue("version", engineVersion);
        command.Parameters.AddWithValue("until", cursor.ReplayedUntil);
        command.Parameters.AddWithValue("open", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(cursor.Open.Select(SignalDbConverter.FromDomain).ToList()));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IdeaReplayProgress> ProgressAsync(int engineVersion, DateTimeOffset historyStart, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        Progress row = await connection.QuerySingleAsync<Progress>(
            new CommandDefinition(
                """
                -- reached: where a turn of the pair would go on from (IdeasReplay.Next: a cursor older than the start of the pair starts it over);
                -- needed: the day before the last whole day of its coverage, so a pair that waits for the day just closed is still done.
                WITH pairs AS (
                    SELECT greatest(@HistoryStart, date_trunc('day', p.history_from + interval '24 hours' - interval '1 microsecond', 'UTC'), r.replayed_until) AS reached,
                           date_trunc('day', p.covered_until, 'UTC') - interval '24 hours' AS needed,
                           r.replayed_until IS NOT NULL AS has_cursor
                    FROM scanner_pairs p
                    LEFT JOIN idea_replay r ON r.category = p.category AND r.symbol = p.symbol AND r.engine_version = @Version
                    WHERE p.listed AND p.history_from IS NOT NULL AND p.covered_until IS NOT NULL AND p.tick_size IS NOT NULL
                )
                SELECT count(*) FILTER (WHERE reached >= needed)::int AS "Done",
                       count(*)::int AS "Total",
                       min(reached) FILTER (WHERE reached < needed AND has_cursor) AS "Slowest"
                FROM pairs
                """,
                new { Version = engineVersion, HistoryStart = historyStart },
                cancellationToken: cancellationToken));

        return new IdeaReplayProgress(row.Done, row.Total, row.Slowest, engineVersion);
    }

    public async Task DeleteOtherVersionsAsync(int engineVersion, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(
            new CommandDefinition("DELETE FROM idea_replay WHERE engine_version <> @Version", new { Version = engineVersion }, cancellationToken: cancellationToken));
    }

    private sealed class Progress
    {
        public int Done { get; init; }

        public int Total { get; init; }

        public DateTimeOffset? Slowest { get; init; }
    }

    private sealed class Cursor
    {
        public required string Category { get; init; }

        public required string Symbol { get; init; }

        public DateTimeOffset ReplayedUntil { get; init; }

        public required string OpenSignals { get; init; }
    }
}