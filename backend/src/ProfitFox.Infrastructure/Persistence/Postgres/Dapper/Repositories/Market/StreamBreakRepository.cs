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

    public async Task<IReadOnlyList<StreamBreak>> GetAsync(string stream, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            "SELECT lost_at, restored_at FROM stream_breaks WHERE stream = @stream AND lost_at < @to AND restored_at > @from ORDER BY lost_at");
        command.Parameters.AddWithValue("stream", stream);
        command.Parameters.AddWithValue("from", from);
        command.Parameters.AddWithValue("to", to);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var breaks = new List<StreamBreak>();

        // Npgsql reads timestamptz as a UTC DateTimeOffset only when asked for one; Dapper would get a DateTime.
        while (await reader.ReadAsync(cancellationToken))
        {
            breaks.Add(new StreamBreak(stream, reader.GetFieldValue<DateTimeOffset>(0), reader.GetFieldValue<DateTimeOffset>(1)));
        }

        return breaks;
    }
}