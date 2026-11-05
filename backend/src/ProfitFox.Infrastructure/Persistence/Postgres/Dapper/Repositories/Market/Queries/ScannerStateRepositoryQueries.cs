using Dapper;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Queries;

internal static class ScannerStateRepositoryQueries
{
    // One statement: the check and the take are one step, so two scanners never both take the lease.
    private const string ClaimSql = """
                                    UPDATE scanner_state SET lease_owner = @Owner, leased_until = @LeasedUntil
                                    WHERE id = 1 AND (leased_until IS NULL OR leased_until <= @Now OR lease_owner = @Owner)
                                    """;

    private const string RenewSql = "UPDATE scanner_state SET leased_until = @LeasedUntil WHERE id = 1 AND lease_owner = @Owner";

    // A pause only ever ends later; GREATEST skips the NULL of a scanner that was never paused.
    private const string PauseRestSql = "UPDATE scanner_state SET rest_paused_until = GREATEST(rest_paused_until, @Until) WHERE id = 1";

    private const string GetSql = """
                                  SELECT snapshot_at AS "SnapshotAt", pairs_total AS "PairsTotal", pairs_backfilled AS "PairsBackfilled",
                                      rest_paused_until AS "RestPausedUntil"
                                  FROM scanner_state
                                  WHERE id = 1
                                  """;

    internal static CommandDefinition SetProgress(int pairsTotal, int pairsBackfilled, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            "UPDATE scanner_state SET pairs_total = @PairsTotal, pairs_backfilled = @PairsBackfilled WHERE id = 1",
            new { PairsTotal = pairsTotal, PairsBackfilled = pairsBackfilled },
            cancellationToken: cancellationToken);
    }

    internal static CommandDefinition PauseRest(DateTimeOffset until, CancellationToken cancellationToken)
    {
        return new CommandDefinition(PauseRestSql, new { Until = until }, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition Get(CancellationToken cancellationToken)
    {
        return new CommandDefinition(GetSql, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition Claim(Guid owner, DateTimeOffset now, DateTimeOffset leasedUntil, CancellationToken cancellationToken)
    {
        return new CommandDefinition(ClaimSql, new { Owner = owner, Now = now, LeasedUntil = leasedUntil }, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition Renew(Guid owner, DateTimeOffset leasedUntil, CancellationToken cancellationToken)
    {
        return new CommandDefinition(RenewSql, new { Owner = owner, LeasedUntil = leasedUntil }, cancellationToken: cancellationToken);
    }
}