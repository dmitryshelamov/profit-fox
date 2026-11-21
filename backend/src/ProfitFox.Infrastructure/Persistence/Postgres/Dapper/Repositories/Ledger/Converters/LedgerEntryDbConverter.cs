using ProfitFox.Domain.ValueObjects.Ledger;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ledger.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ledger.Converters;

internal static class LedgerEntryDbConverter
{
    /// <summary>
    ///     The parameters of a row of <c>ledger</c>: the values of the line, its time in UTC.
    /// </summary>
    internal static object FromDomain(LedgerEntry entry)
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

    internal static LedgerEntry ToDomain(LedgerEntryDb row)
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
