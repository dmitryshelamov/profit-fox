using System.Text.Json;

using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.Filters;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Lists;
using ProfitFox.Domain.ValueObjects.Lists;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Filters;

/// <summary>
///     The saved filters in <c>saved_filters</c> (change add-ideas-and-market, design.md, decision 11): the conditions as
///     jsonb, the name one of its kind by a unique constraint.
/// </summary>
internal sealed class SavedFilterRepository(NpgsqlDataSource database) : ISavedFilterRepository
{
    private const string UniqueViolation = "23505";
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private const string Columns = """id AS "Id", name AS "Name", place AS "Place", conditions::text AS "Conditions" """;

    public async Task<IReadOnlyList<SavedFilter>> AllAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        return [.. (await connection.QueryAsync<Row>(new CommandDefinition($"SELECT {Columns} FROM saved_filters ORDER BY name", cancellationToken: cancellationToken))).Select(row => row.ToDomain())];
    }

    public async Task<SavedFilter?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        Row? row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition($"SELECT {Columns} FROM saved_filters WHERE id = @Id", new { Id = id }, cancellationToken: cancellationToken));
        return row?.ToDomain();
    }

    public async Task<bool> SaveAsync(SavedFilter filter, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO saved_filters (id, name, place, conditions) VALUES (@Id, @Name, @Place, @Conditions::jsonb)
                ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name, place = EXCLUDED.place, conditions = EXCLUDED.conditions
                """,
                new { filter.Id, filter.Name, Place = FilterRules.Name(filter.Place), Conditions = JsonSerializer.Serialize(filter.Conditions, Json) },
                cancellationToken: cancellationToken));
            return true;
        }
        catch (PostgresException exception) when (exception.SqlState == UniqueViolation)
        {
            return false;
        }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition("DELETE FROM saved_filters WHERE id = @Id", new { Id = id }, cancellationToken: cancellationToken)) == 1;
    }

    private sealed class Row
    {
        public Guid Id { get; init; }

        public string Name { get; init; } = "";

        public string Place { get; init; } = "";

        public string Conditions { get; init; } = "[]";

        public SavedFilter ToDomain()
        {
            return new SavedFilter(Id, Name, FilterRules.Place(Place)!.Value, JsonSerializer.Deserialize<FilterCondition[]>(Conditions, Json) ?? []);
        }
    }
}
