using Dapper;

using Npgsql;

using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Trades;

/// <summary>
///     The lease of the trading Worker in the one row of <c>trader_lease</c> (change take-idea-trades, design.md, decision 2):
///     taken, renewed and given up by one statement each on the time of the database, with the terms of the lease of the
///     scanner.
/// </summary>
internal sealed class TraderLeaseRepository(NpgsqlDataSource database) : ITraderLeaseRepository
{
    public async Task<bool> TakeAsync(string worker, TimeSpan duration, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        string? holder = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            INSERT INTO trader_lease (name, worker, until, polled_at) VALUES (@Name, @Worker, now() + @Duration, now())
            ON CONFLICT (name) DO UPDATE SET worker = EXCLUDED.worker, until = EXCLUDED.until, polled_at = EXCLUDED.polled_at
            WHERE trader_lease.worker = EXCLUDED.worker OR trader_lease.until < now()
            RETURNING worker
            """,
            new { TraderFence.Name, Worker = worker, Duration = duration },
            cancellationToken: cancellationToken));
        return holder == worker;
    }

    public async Task ReleaseAsync(string worker, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE trader_lease SET until = now() WHERE name = @Name AND worker = @Worker",
            new { TraderFence.Name, Worker = worker },
            cancellationToken: cancellationToken));
    }

    public async Task<TraderState?> StateAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        Row? row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            """SELECT worker AS "Worker", until AS "Until", now() AS "Now" FROM trader_lease WHERE name = @Name""",
            new { TraderFence.Name },
            cancellationToken: cancellationToken));
        return row is null ? null : new TraderState(row.Worker, row.Until, row.Now);
    }

    private sealed class Row
    {
        public string Worker { get; init; } = "";

        public DateTimeOffset Until { get; init; }

        public DateTimeOffset Now { get; init; }
    }
}
