using ProfitFox.Domain.ValueObjects.Ledger;

namespace ProfitFox.Application.Contracts.Runs.Converters;

public static class LedgerEntryInternalConverter
{
    public static LedgerEntryInternal ToInternal(this LedgerEntry entry)
    {
        return new LedgerEntryInternal
        {
            RunId = entry.RunId,
            Time = entry.Time,
            Hypothesis = entry.Hypothesis,
            Hash = entry.Hash,
            Version = entry.Version,
            EngineVersion = entry.EngineVersion,
            Period = entry.Period,
            Status = entry.Status,
            DataHash = entry.DataHash,
            Attempts = entry.Attempts,
            Reason = entry.Reason,
            RequiredT = entry.RequiredT,
            Power = entry.Power,
            Blocks = entry.Blocks,
            Events = entry.Events,
            T = entry.T,
            Mean = entry.Mean,
            FirstHalfMean = entry.FirstHalfMean,
            SecondHalfMean = entry.SecondHalfMean,
            ReadSeconds = entry.ReadSeconds,
            Corrects = entry.Corrects
        };
    }
}
