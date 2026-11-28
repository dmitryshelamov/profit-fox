namespace ProfitFox.Domain.ValueObjects.Trades;

/// <summary>
///     The emergency stop (change take-idea-trades, design.md, decision 11): when it went off, at what loss of the day and
///     what limit — null while entries are allowed —, and when the ban was last lifted.
/// </summary>
public sealed record TradeHalt(DateTimeOffset? HaltedAt, decimal? Loss, decimal? Limit, DateTimeOffset? LiftedAt)
{
    public static readonly TradeHalt None = new TradeHalt(null, null, null, null);

    public bool Halted => HaltedAt is not null;
}
