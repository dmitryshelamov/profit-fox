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
                """SELECT category, symbol, replayed_until AS "ReplayedUntil", open_ideas::text AS "OpenIdeas" FROM idea_replay WHERE engine_version = @Version""",
                new { Version = engineVersion },
                cancellationToken: cancellationToken));

        return rows.ToDictionary(
            row =>
            {
                MarketDataCodes.TryParseCategory(row.Category, out MarketCategory category);

                return new Instrument(category, row.Symbol);
            },
            row => new IdeaReplayCursor(row.ReplayedUntil, [.. JsonSerializer.Deserialize<List<IdeaDb>>(row.OpenIdeas)!.Select(IdeaDbConverter.ToDomain)]));
    }

    public async Task SetAsync(Instrument instrument, int engineVersion, IdeaReplayCursor cursor, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            """
            INSERT INTO idea_replay (category, symbol, engine_version, replayed_until, open_ideas) VALUES (@category, @symbol, @version, @until, @open)
            ON CONFLICT (category, symbol, engine_version) DO UPDATE SET replayed_until = EXCLUDED.replayed_until, open_ideas = EXCLUDED.open_ideas
            """);
        command.Parameters.AddWithValue("category", instrument.Category.ToCode());
        command.Parameters.AddWithValue("symbol", instrument.Symbol);
        command.Parameters.AddWithValue("version", engineVersion);
        command.Parameters.AddWithValue("until", cursor.ReplayedUntil);
        command.Parameters.AddWithValue("open", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(cursor.Open.Select(idea => IdeaDbConverter.FromDomain(idea)).ToList()));
        await command.ExecuteNonQueryAsync(cancellationToken);
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

        public required string OpenIdeas { get; init; }
    }
}