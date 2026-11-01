using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Entities.MarketData;

/// <summary>
///     A request to load candles of one instrument and interval for <c>[From, To)</c>, executed by a Worker under a lease.
///     <see cref="LostLeases" /> counts leases in a row that expired on the running job before it stored a candle.
/// </summary>
public sealed record CandleImportJob(
    Guid Id,
    Instrument Instrument,
    Interval Interval,
    DateTimeOffset From,
    DateTimeOffset To,
    CandleImportStatus Status,
    long ImportedCandles,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    int LostLeases = 0)
{
    public const string InterruptedWithoutProgress = "Interrupted three times in a row without progress.";

    // A job that keeps stopping its Worker before it stores anything would otherwise be taken over forever.
    private const int MaxLostLeases = 3;

    /// <summary>
    ///     The job as a Worker takes it: a pending one starts running; a running one, whose lease expired, counts one more lost lease,
    ///     and the third one fails it.
    /// </summary>
    public CandleImportJob TakenOver(DateTimeOffset now)
    {
        if (Status == CandleImportStatus.Pending)
        {
            return this with { Status = CandleImportStatus.Running, StartedAt = now };
        }

        CandleImportJob taken = this with { LostLeases = LostLeases + 1 };

        return taken.LostLeases == MaxLostLeases
            ? taken with { Status = CandleImportStatus.Failed, Error = InterruptedWithoutProgress, FinishedAt = now }
            : taken;
    }
}