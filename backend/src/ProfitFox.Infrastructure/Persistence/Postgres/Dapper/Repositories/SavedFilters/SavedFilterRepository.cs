using Dapper;

using Npgsql;

using NpgsqlTypes;

using ProfitFox.Application.SavedFilters;
using ProfitFox.Domain.Entities.SavedFilters;
using ProfitFox.Domain.Entities.SavedFilters.Enums;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.SavedFilters;

internal sealed class SavedFilterRepository(NpgsqlDataSource dataSource) : ISavedFilterRepository
{
    private const string Columns = """id, name, place, conditions::text AS "Conditions", created_at AS "CreatedAt" """;

    public async Task<IReadOnlyList<SavedFilter>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<Row> rows = await connection.QueryAsync<Row>(
            new CommandDefinition($"SELECT {Columns} FROM saved_filters ORDER BY created_at, id", cancellationToken: cancellationToken));

        return [.. rows.Select(Filter)];
    }

    public async Task<SavedFilter?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        Row? row = await connection.QuerySingleOrDefaultAsync<Row>(
            new CommandDefinition($"SELECT {Columns} FROM saved_filters WHERE id = @Id", new { Id = id }, cancellationToken: cancellationToken));

        return row is null ? null : Filter(row);
    }

    public async Task AddAsync(SavedFilter filter, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            "INSERT INTO saved_filters (id, name, place, conditions, created_at) VALUES (@id, @name, @place, @conditions, @created_at)");
        Parameters(command, filter);
        command.Parameters.AddWithValue("created_at", filter.CreatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> UpdateAsync(SavedFilter filter, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("UPDATE saved_filters SET name = @name, place = @place, conditions = @conditions WHERE id = @id");
        Parameters(command, filter);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(
            new CommandDefinition("DELETE FROM saved_filters WHERE id = @Id", new { Id = id }, cancellationToken: cancellationToken)) > 0;
    }

    private static void Parameters(NpgsqlCommand command, SavedFilter filter)
    {
        command.Parameters.AddWithValue("id", filter.Id);
        command.Parameters.AddWithValue("name", filter.Name);
        command.Parameters.AddWithValue("place", filter.Place.ToCode());
        command.Parameters.AddWithValue("conditions", NpgsqlDbType.Jsonb, SavedFilterJson.Write(filter.Conditions));
    }

    private static SavedFilter Filter(Row row)
    {
        SavedFilterFields.TryParsePlace(row.Place, out SavedFilterPlace place);

        return new SavedFilter(row.Id, row.Name, place, SavedFilterJson.Read(row.Conditions), row.CreatedAt);
    }

    private sealed class Row
    {
        public Guid Id { get; init; }

        public required string Name { get; init; }

        public required string Place { get; init; }

        public required string Conditions { get; init; }

        public DateTimeOffset CreatedAt { get; init; }
    }
}