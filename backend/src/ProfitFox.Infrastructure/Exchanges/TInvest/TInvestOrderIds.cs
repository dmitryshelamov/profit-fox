using Dapper;

using Npgsql;

namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>
///     <c>moex_orders</c> on a connection of its own for every call, outside any unit of work of a session: a row written before a send
///     that never happened stays, and the next send of the order goes with it.
/// </summary>
internal sealed class TInvestOrderIds(NpgsqlDataSource dataSource, TimeProvider time) : ITInvestOrderIds
{
    public async Task<Guid> EnsureAsync(string clientId, string accountId, string instrumentUid, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        // Two sends of one order at once both read the row the first of them wrote.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO moex_orders (client_id, account_id, request_id, instrument_uid, created_at)
            VALUES (@clientId, @accountId, @requestId, @instrumentUid, @createdAt)
            ON CONFLICT (client_id) DO NOTHING
            """,
            new { clientId, accountId, requestId = Guid.NewGuid(), instrumentUid, createdAt = time.GetUtcNow() },
            cancellationToken: cancellationToken));

        return await connection.QuerySingleAsync<Guid>(new CommandDefinition(
            "SELECT request_id FROM moex_orders WHERE client_id = @clientId",
            new { clientId },
            cancellationToken: cancellationToken));
    }

    public async Task<Guid?> RequestOfAsync(string clientId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            "SELECT request_id FROM moex_orders WHERE client_id = @clientId",
            new { clientId },
            cancellationToken: cancellationToken));
    }

    public async Task<string?> ClientIdOfAsync(Guid requestId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT client_id FROM moex_orders WHERE request_id = @requestId",
            new { requestId },
            cancellationToken: cancellationToken));
    }

    public async Task SetExchangeIdAsync(string clientId, string exchangeOrderId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE moex_orders SET exchange_order_id = @exchangeOrderId WHERE client_id = @clientId",
            new { clientId, exchangeOrderId },
            cancellationToken: cancellationToken));
    }
}