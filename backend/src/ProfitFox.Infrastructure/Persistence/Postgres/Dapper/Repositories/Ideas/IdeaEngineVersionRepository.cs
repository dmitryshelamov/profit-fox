using Dapper;

using Npgsql;

using ProfitFox.Domain.Repositories;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;

/// <summary>The start of each version of the rules of the ideas, one row per version of <c>idea_engine_versions</c>.</summary>
internal sealed class IdeaEngineVersionRepository(NpgsqlDataSource dataSource) : IIdeaEngineVersionRepository
{
    public async Task StartAsync(int engineVersion, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO idea_engine_versions (engine_version, started_at) VALUES (@Version, @At) ON CONFLICT DO NOTHING",
            new { Version = engineVersion, At = at },
            cancellationToken: cancellationToken));
    }

    public async Task<DateTimeOffset?> GetStartAsync(int engineVersion, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        Row? row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            """SELECT started_at AS "StartedAt" FROM idea_engine_versions WHERE engine_version = @Version""",
            new { Version = engineVersion },
            cancellationToken: cancellationToken));

        return row?.StartedAt;
    }

    private sealed class Row
    {
        public DateTimeOffset StartedAt { get; init; }
    }
}