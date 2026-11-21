namespace ProfitFox.Domain.Trades;

/// <summary>
///     The outcome of a trade (spec trade-plan-outcome). Results are fractions of the trade amount; <see cref="R" /> is the
///     result after costs in units of the stop distance and is null without a stop. A trade left out has only its status.
/// </summary>
public sealed record TradeOutcome(
    TradeStatus Status,
    ExitReason ExitReason,
    bool Ambiguous,
    DateTimeOffset? EntryTime,
    DateTimeOffset? ExitTime,
    double? EntryPrice,
    double? ExitPrice,
    double? Gross,
    double? Fees,
    double? Slippage,
    double? Funding,
    double? Net,
    double? R)
{
    public static TradeOutcome Excluded(TradeStatus status)
    {
        return new TradeOutcome(status, ExitReason.None, Ambiguous: false, null, null, null, null, null, null, null, null, null, null);
    }
}
