using Dapper;

using Npgsql;

using ProfitFox.Domain.Exceptions;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Trades;

/// <summary>
///     The fence of the writes of the trading Worker (change take-idea-trades, design.md, decision 2): a transaction of the
///     Worker first takes the row of <c>trader_lease</c> under <c>FOR UPDATE</c> and checks that the Worker holds it and that
///     it has not run out on the time of the database.
/// </summary>
internal static class TraderFence
{
    public const string Name = "trader";

    /// <summary>
    ///     The key of the advisory lock of the entry, «take» in ASCII: the entry and the emergency stop take it in their
    ///     transactions, so an entry is never written while the stop goes off.
    /// </summary>
    public const long EntryLock = 0x74616b65;

    /// <exception cref="LeaseLostException">The Worker does not hold the lease.</exception>
    public static async Task CheckAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string? worker, CancellationToken cancellationToken)
    {
        if (worker is null)
        {
            return;
        }

        bool held = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM trader_lease WHERE name = @Name AND worker = @Worker AND until > now() FOR UPDATE)",
            new { Name, Worker = worker },
            transaction,
            cancellationToken: cancellationToken));
        if (!held)
        {
            throw LeaseLostException.OfTrader(worker);
        }
    }
}
