using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Domain.ValueObjects.Runs;

/// <summary>
///     An order of a pair with what a backtest knows of it: its week, whether its trade counts, its outcome and its random
///     entries.
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
    TradeFields? RandomMean)
{
    /// <summary>
    ///     The result of the cycle of the order in USDT; null when its result is not computed.
    /// </summary>
    public double? NetAmount { get; init; }

    /// <summary>
    ///     The order counts in the difference from random entries: its trade is not left out and it has at least two valid
    ///     random entries.
    /// </summary>
    public bool Usable => Status == TradeStatus.Ok && RandomValid >= 2;
}
