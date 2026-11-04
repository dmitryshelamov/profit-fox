using Dapper;

using Npgsql;

using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;

internal sealed class IdeaReplayRepository(NpgsqlDataSource dataSource) : IIdeaReplayRepository
{
    public async Task<IReadOnlyDictionary<Instrument, DateTimeOffset>> GetAsync(int engineVersion, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<Cursor> rows = await connection.QueryAsync<Cursor>(
            new CommandDefinition(
                """SELECT category, symbol, replayed_until AS "ReplayedUntil" FROM idea_replay WHERE engine_version = @Version""",
                new { Version = engineVersion },
                cancellationToken: cancellationToken));

        return rows.ToDictionary(
            row =>
            {
                MarketDataCodes.TryParseCategory(row.Category, out MarketCategory category);

                return new Instrument(category, row.Symbol);
            },
            row => row.ReplayedUntil);
    }

    public async Task SetAsync(Instrument instrument, int engineVersion, DateTimeOffset replayedUntil, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO idea_replay (category, symbol, engine_version, replayed_until) VALUES (@Category, @Symbol, @Version, @Until)
                ON CONFLICT (category, symbol, engine_version) DO UPDATE SET replayed_until = EXCLUDED.replayed_until
                """,
                new { Category = instrument.Category.ToCode(), instrument.Symbol, Version = engineVersion, Until = replayedUntil },
                cancellationToken: cancellationToken));
    }

    public async Task DeleteOtherVersionsAsync(int engineVersion, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(
            new CommandDefinition("DELETE FROM idea_replay WHERE engine_version <> @Version", new { Version = engineVersion }, cancellationToken: cancellationToken));
    }

    private sealed class Cursor
    {
        public required string Category { get; init; }

        public required string Symbol { get; init; }

        public DateTimeOffset ReplayedUntil { get; init; }
    }
}