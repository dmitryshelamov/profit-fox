using System.Data;

using Dapper;

using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData.Queries;

internal static class CandleImportJobRepositoryQueries
{
    private const string InsertSql = """
                                     INSERT INTO candle_import_jobs
                                         (id, category, symbol, "interval", "from", "to", status, imported_candles, error, created_at, started_at, finished_at, lost_leases)
                                     VALUES
                                         (@Id, @Category, @Symbol, @Interval, @From, @To, @Status, @ImportedCandles, @Error, @CreatedAt, @StartedAt, @FinishedAt, @LostLeases)
                                     """;

    private const string GetSql = """
                                  SELECT id, category, symbol, "interval", "from" AS "From", "to" AS "To", status, imported_candles AS "ImportedCandles", error,
                                      created_at AS "CreatedAt", started_at AS "StartedAt", finished_at AS "FinishedAt",
                                      lost_leases AS "LostLeases"
                                  FROM candle_import_jobs
                                  WHERE id = @Id
                                  """;

    // Held until the claim's transaction ends: it makes the check and the claim one step for all Workers, so two of them never take two
    // jobs at once. The claim is a statement of its own, so it sees every claim committed before the lock was granted.
    private const string LockClaimsSql = "SELECT pg_advisory_xact_lock(@LockKey)";

    private const string ClaimNextSql = """
                                        UPDATE candle_import_jobs
                                        SET lease_owner = @Owner, leased_until = @LeasedUntil
                                        WHERE id = (
                                            SELECT id
                                            FROM candle_import_jobs
                                            WHERE status IN (@Pending, @Running) AND (leased_until IS NULL OR leased_until <= @Now)
                                            ORDER BY created_at, id
                                            LIMIT 1)
                                          AND NOT EXISTS (
                                            SELECT 1 FROM candle_import_jobs WHERE status IN (@Pending, @Running) AND leased_until > @Now)
                                        RETURNING id, category, symbol, "interval", "from" AS "From", "to" AS "To", status, imported_candles AS "ImportedCandles", error,
                                            created_at AS "CreatedAt", started_at AS "StartedAt", finished_at AS "FinishedAt",
                                            lost_leases AS "LostLeases"
                                        """;

    private const string RenewLeaseSql = """
                                         UPDATE candle_import_jobs SET leased_until = @LeasedUntil
                                         WHERE id = @Id AND lease_owner = @Owner
                                         """;

    private const string UpdateSql = """
                                     UPDATE candle_import_jobs
                                     SET status = @Status, imported_candles = @ImportedCandles, error = @Error, started_at = @StartedAt, finished_at = @FinishedAt,
                                         lost_leases = @LostLeases
                                     WHERE id = @Id
                                     """;

    internal static CommandDefinition Insert(CandleImportJobDb job, CancellationToken cancellationToken, IDbTransaction? transaction = null)
    {
        return new CommandDefinition(InsertSql, job, transaction, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition Get(Guid id, CancellationToken cancellationToken)
    {
        return new CommandDefinition(GetSql, new { Id = id }, cancellationToken: cancellationToken);
    }

    // Any number of the same key: it only has to differ from other advisory locks of the database, and there are none.
    private const long ClaimLockKey = 4_241_001;

    internal static CommandDefinition LockClaims(IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(LockClaimsSql, new { LockKey = ClaimLockKey }, transaction, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition ClaimNext(Guid owner, DateTimeOffset now, DateTimeOffset leasedUntil, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            ClaimNextSql,
            new
            {
                Owner = owner,
                Now = now,
                LeasedUntil = leasedUntil,
                Pending = nameof(CandleImportStatus.Pending),
                Running = nameof(CandleImportStatus.Running)
            },
            transaction,
            cancellationToken: cancellationToken);
    }

    internal static CommandDefinition RenewLease(Guid id, Guid owner, DateTimeOffset leasedUntil, CancellationToken cancellationToken)
    {
        return new CommandDefinition(RenewLeaseSql, new { Id = id, Owner = owner, LeasedUntil = leasedUntil }, cancellationToken: cancellationToken);
    }

    // A finished job keeps no lease, so a retried one is free for any Worker at once.
    internal static CommandDefinition UpdateLeased(CandleImportJobDb job, Guid owner, CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters(job);
        parameters.Add("Owner", owner);
        parameters.Add("Pending", nameof(CandleImportStatus.Pending));
        parameters.Add("Running", nameof(CandleImportStatus.Running));

        return new CommandDefinition(
            $"""
             {UpdateSql}
               AND lease_owner = @Owner;
             UPDATE candle_import_jobs SET lease_owner = NULL, leased_until = NULL
             WHERE id = @Id AND lease_owner = @Owner AND status NOT IN (@Pending, @Running)
             """,
            parameters,
            cancellationToken: cancellationToken);
    }

    internal static CommandDefinition Update(CandleImportJobDb job, CancellationToken cancellationToken)
    {
        return new CommandDefinition(UpdateSql, job, cancellationToken: cancellationToken);
    }
}