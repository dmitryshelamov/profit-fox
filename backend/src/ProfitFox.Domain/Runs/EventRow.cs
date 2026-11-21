using ProfitFox.Domain.Trades;

namespace ProfitFox.Domain.Runs;

/// <summary>
///     An event of a pair with what the run knows of it: its block, whether its trade counts, its random entries, and —
///     only when the outcomes of events are computed — its own outcome. <see cref="PseudoDifference" /> is the first valid
///     random entry minus the mean of the others: the blind power estimate uses it instead of the event.
/// </summary>
public sealed record EventRow(
    string Instrument,
    DateTimeOffset Moment,
    long Block,
    TradeStatus Status,
    TradeOutcome? Outcome,
    TradeFields? Event,
    IReadOnlyList<RandomTrade> Randoms,
    int RandomValid,
    TradeFields? RandomMean,
    double? PseudoDifference)
{
    /// <summary>
    ///     The event counts: its trade is not left out and it has at least two valid random entries.
    /// </summary>
    public bool Usable => Status == TradeStatus.Ok && RandomValid >= 2;
}
