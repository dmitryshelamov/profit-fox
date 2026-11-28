using Dapper;

using Npgsql;

using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Trades;

/// <summary>
///     The emergency stop in the one row of <c>trade_halt</c> (change take-idea-trades, design.md, decision 11). It goes off
///     in one transaction under the lease of the trading Worker and the advisory lock of the entry, with the close orders of
///     the open trades and the cancel of the entries never tried; an entry tried before may be at the exchange, and the
///     trading Worker asks the exchange about it before it gives it up. The entry checks the ban under the same lock.
/// </summary>
internal sealed class TradeHaltRepository(NpgsqlDataSource database) : ITradeHaltRepository
{
    public async Task<TradeHalt> GetAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        Row? row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            """SELECT halted_at AS "HaltedAt", loss AS "Loss", limit_value AS "Limit", lifted_at AS "LiftedAt" FROM trade_halt WHERE id = 1""",
            cancellationToken: cancellationToken));
        return row is null ? TradeHalt.None : new TradeHalt(row.HaltedAt, row.Loss, row.Limit, row.LiftedAt);
    }

    public async Task<bool> HaltAsync(string worker, decimal loss, decimal limit, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        // The lock of the entry first: an entry counted before the stop is written before it, one after it sees the ban. The
        // row of the lease is taken only after it, so the renewal of the lease does not wait while the stop waits for an entry.
        await connection.ExecuteAsync(new CommandDefinition("SELECT pg_advisory_xact_lock(@Key)", new { Key = TraderFence.EntryLock }, transaction, cancellationToken: cancellationToken));
        await TraderFence.CheckAsync(connection, transaction, worker, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("INSERT INTO trade_halt (id) VALUES (1) ON CONFLICT (id) DO NOTHING", transaction: transaction, cancellationToken: cancellationToken));
        bool halted = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT halted_at IS NOT NULL FROM trade_halt WHERE id = 1 FOR UPDATE",
            transaction: transaction,
            cancellationToken: cancellationToken));
        if (halted)
        {
            return false;
        }

        var values = new { At = at, Loss = loss, Limit = limit };
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE trade_halt SET halted_at = @At, loss = @Loss, limit_value = @Limit WHERE id = 1;

            INSERT INTO trade_orders (trade_id, number, purpose, reason, quantity, status, attempts, next_attempt_at, created_at)
            SELECT t.id, coalesce(max(o.number), 0) + 1, 'close', 'halt', t.held, 'pending', 0, @At, @At
            FROM trades t LEFT JOIN trade_orders o ON o.trade_id = t.id
            WHERE t.state = 'open'
            GROUP BY t.id, t.held;

            UPDATE trades SET state = 'closing' WHERE state = 'open';

            WITH unsent AS (
                UPDATE trade_orders o SET status = 'cancelled'
                FROM trades t
                WHERE o.trade_id = t.id AND t.state = 'pending' AND o.purpose = 'entry' AND o.status = 'pending' AND o.attempts = 0
                RETURNING o.trade_id)
            UPDATE trades SET state = 'cancelled', reason = 'стоп-кран', closed_at = @At WHERE id IN (SELECT trade_id FROM unsent);
            """,
            values,
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> LiftAsync(DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("INSERT INTO trade_halt (id) VALUES (1) ON CONFLICT (id) DO NOTHING", transaction: transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition("SELECT 1 FROM trade_halt WHERE id = 1 FOR UPDATE", transaction: transaction, cancellationToken: cancellationToken));
        if (await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS (SELECT 1 FROM trades WHERE state = 'closing')", transaction: transaction, cancellationToken: cancellationToken)))
        {
            return false;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE trade_halt SET halted_at = NULL, loss = NULL, limit_value = NULL, lifted_at = @At WHERE id = 1",
            new { At = at },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private sealed class Row
    {
        public DateTimeOffset? HaltedAt { get; init; }

        public decimal? Loss { get; init; }

        public decimal? Limit { get; init; }

        public DateTimeOffset? LiftedAt { get; init; }
    }
}
