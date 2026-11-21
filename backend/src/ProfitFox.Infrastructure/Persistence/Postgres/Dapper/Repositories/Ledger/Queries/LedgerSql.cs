using ProfitFox.Domain.ValueObjects.Ledger;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ledger.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ledger.Queries;

/// <summary>
///     The statements on <c>ledger</c> and <c>reports</c> that the ledger uses, and the two ways between a
///     <see cref="LedgerEntry" /> and a row.
/// </summary>
internal static class LedgerSql
{
    public const string Select =
        """
        SELECT run_id AS "RunId", time AS "Time", hypothesis_id AS "Hypothesis", hash AS "Hash", version AS "Version",
               engine_version AS "EngineVersion", period AS "Period", status AS "Status",
               data_hash AS "DataHash", attempts AS "Attempts", reason AS "Reason", required_t AS "RequiredT", power AS "Power",
               blocks AS "Blocks", events AS "Events", t AS "T", mean AS "Mean", first_half_mean AS "FirstHalfMean",
               second_half_mean AS "SecondHalfMean", read_seconds AS "ReadSeconds", corrects AS "Corrects"
        FROM ledger
        ORDER BY seq
        """;

    public const string SelectOne =
        """
        SELECT run_id AS "RunId", time AS "Time", hypothesis_id AS "Hypothesis", hash AS "Hash", version AS "Version",
               engine_version AS "EngineVersion", period AS "Period", status AS "Status",
               data_hash AS "DataHash", attempts AS "Attempts", reason AS "Reason", required_t AS "RequiredT", power AS "Power",
               blocks AS "Blocks", events AS "Events", t AS "T", mean AS "Mean", first_half_mean AS "FirstHalfMean",
               second_half_mean AS "SecondHalfMean", read_seconds AS "ReadSeconds", corrects AS "Corrects"
        FROM ledger
        WHERE run_id = @RunId
        """;

    public const string SelectRecent =
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

    public const string Insert =
        """
        INSERT INTO ledger (run_id, time, hypothesis_id, hash, version, engine_version, period, status, data_hash, attempts,
                            reason, required_t, power, blocks, events, t, mean, first_half_mean, second_half_mean, read_seconds, corrects)
        VALUES (@RunId, @Time, @Hypothesis, @Hash, @Version, @EngineVersion, @Period, @Status, @DataHash, @Attempts,
                @Reason, @RequiredT, @Power, @Blocks, @Events, @T, @Mean, @FirstHalfMean, @SecondHalfMean, @ReadSeconds, @Corrects)
        """;

    public const string InsertReport = "INSERT INTO reports (run_id, text) VALUES (@RunId, @Text)";

    public static object Parameters(LedgerEntry entry)
    {
        return new
        {
            entry.RunId,
            Time = entry.Time.ToUniversalTime(),
            entry.Hypothesis,
            entry.Hash,
            entry.Version,
            entry.EngineVersion,
            entry.Period,
            entry.Status,
            entry.DataHash,
            entry.Attempts,
            entry.Reason,
            entry.RequiredT,
            entry.Power,
            entry.Blocks,
            entry.Events,
            entry.T,
            entry.Mean,
            entry.FirstHalfMean,
            entry.SecondHalfMean,
            entry.ReadSeconds,
            entry.Corrects
        };
    }

    public static LedgerEntry Entry(LedgerRow row)
    {
        return new LedgerEntry
        {
            RunId = row.RunId,
            Time = row.Time,
            Hypothesis = row.Hypothesis,
            Hash = row.Hash,
            Version = row.Version,
            EngineVersion = row.EngineVersion,
            Period = row.Period,
            Status = row.Status,
            DataHash = row.DataHash,
            Attempts = row.Attempts,
            Reason = row.Reason,
            RequiredT = row.RequiredT,
            Power = row.Power,
            Blocks = row.Blocks,
            Events = row.Events,
            T = row.T,
            Mean = row.Mean,
            FirstHalfMean = row.FirstHalfMean,
            SecondHalfMean = row.SecondHalfMean,
            ReadSeconds = row.ReadSeconds,
            Corrects = row.Corrects
        };
    }
}
