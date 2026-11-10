using Dapper;

using Npgsql;

using ProfitFox.Domain.Repositories;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;

internal sealed class TradeFlowStateRepository(NpgsqlDataSource dataSource) : ITradeFlowStateRepository
{
    // One statement: the check and the take are one step, so two collectors never both take the lease.
    private const string ClaimSql = """
                                    UPDATE trade_flow_state SET lease_owner = @Owner, leased_until = @LeasedUntil
                                    WHERE id = 1 AND (leased_until IS NULL OR leased_until <= @Now OR lease_owner = @Owner)
                                    """;

    private const string RenewSql = "UPDATE trade_flow_state SET leased_until = @LeasedUntil WHERE id = 1 AND lease_owner = @Owner";

    public async Task<bool> ClaimAsync(Guid owner, DateTimeOffset now, DateTimeOffset leasedUntil, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(
            new CommandDefinition(ClaimSql, new { Owner = owner, Now = now, LeasedUntil = leasedUntil }, cancellationToken: cancellationToken)) > 0;
    }

    public async Task<bool> RenewAsync(Guid owner, DateTimeOffset leasedUntil, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(new CommandDefinition(RenewSql, new { Owner = owner, LeasedUntil = leasedUntil }, cancellationToken: cancellationToken)) > 0;
    }
}