using System.Data;

using Dapper;

using ProfitFox.Domain.ValueObjects.Ledger;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ledger.Converters;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ledger.Queries;

/// <summary>
///     The statements on <c>ledger</c> and <c>reports</c> that the ledger uses, and those on <c>runs</c> that end a run of the
///     queue in the same step.
/// </summary>
internal static class LedgerRepositoryQueries
{
    private const string EntriesSql =
        """
        SELECT run_id AS "RunId", time AS "Time", hypothesis_id AS "Hypothesis", hash AS "Hash", version AS "Version",
               engine_version AS "EngineVersion", period AS "Period", status AS "Status",
               data_hash AS "DataHash", attempts AS "Attempts", reason AS "Reason", required_t AS "RequiredT", power AS "Power",
               blocks AS "Blocks", events AS "Events", t AS "T", mean AS "Mean", first_half_mean AS "FirstHalfMean",
               second_half_mean AS "SecondHalfMean", read_seconds AS "ReadSeconds", corrects AS "Corrects"
        FROM ledger
        ORDER BY seq
        """;

    private const string FindSql =
        """
        SELECT run_id AS "RunId", time AS "Time", hypothesis_id AS "Hypothesis", hash AS "Hash", version AS "Version",
               engine_version AS "EngineVersion", period AS "Period", status AS "Status",
               data_hash AS "DataHash", attempts AS "Attempts", reason AS "Reason", required_t AS "RequiredT", power AS "Power",
               blocks AS "Blocks", events AS "Events", t AS "T", mean AS "Mean", first_half_mean AS "FirstHalfMean",
               second_half_mean AS "SecondHalfMean", read_seconds AS "ReadSeconds", corrects AS "Corrects"
        FROM ledger
        WHERE run_id = @RunId
        """;

    private const string RecentSql =
        """
        SELECT l.run_id AS "RunId", l.time AS "Time", l.hypothesis_id AS "Hypothesis", l.hash AS "Hash", l.version AS "Version",
               l.engine_version AS "EngineVersion", l.period AS "Period", l.status AS "Status",
               l.data_hash AS "DataHash", l.attempts AS "Attempts", l.reason AS "Reason", l.required_t AS "RequiredT", l.power AS "Power",
               l.blocks AS "Blocks", l.events AS "Events", l.t AS "T", l.mean AS "Mean", l.first_half_mean AS "FirstHalfMean",
               l.second_half_mean AS "SecondHalfMean", l.read_seconds AS "ReadSeconds", l.corrects AS "Corrects",
               r.kind = 'parity' AS "Parity"
        FROM ledger l
        JOIN runs r ON r.id = l.run_id
        ORDER BY l.seq DESC
        LIMIT @Limit
        """;

    private const string InsertSql =
        """
        INSERT INTO ledger (run_id, time, hypothesis_id, hash, version, engine_version, period, status, data_hash, attempts,
                            reason, required_t, power, blocks, events, t, mean, first_half_mean, second_half_mean, read_seconds, corrects)
        VALUES (@RunId, @Time, @Hypothesis, @Hash, @Version, @EngineVersion, @Period, @Status, @DataHash, @Attempts,
                @Reason, @RequiredT, @Power, @Blocks, @Events, @T, @Mean, @FirstHalfMean, @SecondHalfMean, @ReadSeconds, @Corrects)
        """;

    private const string InsertReportSql = "INSERT INTO reports (run_id, text) VALUES (@RunId, @Text)";

    private const string ReportSql = "SELECT text FROM reports WHERE run_id = @RunId";

    private const string LockSql = "SELECT pg_advisory_xact_lock(@Key)";

    private const string HeldSql = "SELECT 1 FROM runs WHERE id = @RunId AND lease_owner = @Worker AND status = 'running' FOR UPDATE";

    private const string FinishSql = "UPDATE runs SET status = 'finished', finished_at = now() WHERE id = @RunId";

    // The lock is held to the end of the transaction. Verdicts are rare and a decision is a few means over blocks, so one lock
    // for all periods costs nothing and cannot be forgotten for a period.
    private const long DecisionLock = 0x424E4348;

    public static CommandDefinition Entries(IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(EntriesSql, transaction: transaction, cancellationToken: cancellationToken);
    }

    public static CommandDefinition Recent(int limit, CancellationToken cancellationToken)
    {
        return new CommandDefinition(RecentSql, new { Limit = limit }, cancellationToken: cancellationToken);
    }

    public static CommandDefinition Find(string runId, CancellationToken cancellationToken)
    {
        return new CommandDefinition(FindSql, new { RunId = runId }, cancellationToken: cancellationToken);
    }

    public static CommandDefinition Report(string runId, CancellationToken cancellationToken)
    {
        return new CommandDefinition(ReportSql, new { RunId = runId }, cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     The one lock of the decisions of the whole bench, to the end of the transaction.
    /// </summary>
    public static CommandDefinition Lock(IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(LockSql, new { Key = DecisionLock }, transaction, cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     Locks the row of the run of <paramref name="assignment" /> while its Worker holds the lease; no row when it does not.
    /// </summary>
    public static CommandDefinition Held(RunAssignment assignment, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(HeldSql, new { assignment.RunId, assignment.Worker }, transaction, cancellationToken: cancellationToken);
    }

    public static CommandDefinition Insert(LedgerEntry entry, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(InsertSql, LedgerEntryDbConverter.FromDomain(entry), transaction, cancellationToken: cancellationToken);
    }

    public static CommandDefinition InsertReport(string runId, string report, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(InsertReportSql, new { RunId = runId, Text = report }, transaction, cancellationToken: cancellationToken);
    }

    public static CommandDefinition Finish(RunAssignment assignment, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(FinishSql, new { assignment.RunId }, transaction, cancellationToken: cancellationToken);
    }
}
