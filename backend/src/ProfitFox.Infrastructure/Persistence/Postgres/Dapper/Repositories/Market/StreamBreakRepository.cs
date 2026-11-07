using Dapper;

using Npgsql;

using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Market;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;

/// <summary>A break from a moment already stored grows to the later end, as when a scanner takes the lease again before a new event.</summary>
internal sealed class StreamBreakRepository(NpgsqlDataSource dataSource) : IStreamBreakRepository
{
    public async Task AddAsync(StreamBreak streamBreak, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO stream_breaks (stream, lost_at, restored_at) VALUES (@Stream, @LostAt, @RestoredAt)
                ON CONFLICT (stream, lost_at) DO UPDATE SET restored_at = GREATEST(stream_breaks.restored_at, EXCLUDED.restored_at)
                """,
                streamBreak,
                cancellationToken: cancellationToken));
    }
}