using Dapper;

using Npgsql;

using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Market;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;

internal sealed class StreamBreakRepository(NpgsqlDataSource dataSource) : IStreamBreakRepository
{
    public async Task AddAsync(StreamBreak streamBreak, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(
            new CommandDefinition(
                "INSERT INTO stream_breaks (stream, lost_at, restored_at) VALUES (@Stream, @LostAt, @RestoredAt)",
                streamBreak,
                cancellationToken: cancellationToken));
    }
}