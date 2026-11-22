namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Queries;

/// <summary>
///     The statements on <c>scanner_lease</c>, the one row of the lease of the scanner, each on the time of the database.
/// </summary>
internal static class ScannerLeaseRepositoryQueries
{
    public const string Name = "scanner";

    // Taken when nobody holds it or the lease ran out, renewed by its holder; a new holder starts with nothing warmed and
    // not live yet.
    public const string Take =
        """
        INSERT INTO scanner_lease (name, worker, until) VALUES (@Name, @Worker, now() + @Duration)
        ON CONFLICT (name) DO UPDATE SET
            worker = EXCLUDED.worker,
            until = EXCLUDED.until,
            live_since = CASE WHEN scanner_lease.worker = EXCLUDED.worker THEN scanner_lease.live_since END,
            warmed = CASE WHEN scanner_lease.worker = EXCLUDED.worker THEN scanner_lease.warmed ELSE 0 END
        WHERE scanner_lease.worker = EXCLUDED.worker OR scanner_lease.until < now()
        RETURNING worker
        """;

    public const string Release = "UPDATE scanner_lease SET until = now(), live_since = NULL WHERE name = @Name AND worker = @Worker";

    public const string Report = "UPDATE scanner_lease SET live_since = @LiveSince, warmed = @Warmed, pairs = @Pairs WHERE name = @Name AND worker = @Worker";

    public const string State =
        """
        SELECT worker AS "Worker", until AS "Until", live_since AS "LiveSince", warmed AS "Warmed", pairs AS "Pairs", now() AS "Now"
        FROM scanner_lease WHERE name = @Name
        """;
}
