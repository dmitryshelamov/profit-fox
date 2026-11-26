using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Domain.ValueObjects.Runs;

/// <summary>
///     An order of a pair with what a backtest knows of it: its side, its week, whether its trade counts and its outcome. Its
///     random entries are not in it: they are the same for every order of a set with its moment and side, and a backtest
///     keeps them in the cells of control (change fix-backtest-random-control, design.md, decision 5).
/// </summary>
public sealed record EventRow(
    string Instrument,
    DateTimeOffset Moment,
    EntrySide Side,
    long Block,
    TradeStatus Status,
    TradeOutcome? Outcome,
    TradeFields? Event)
{
    /// <summary>
    ///     The result of the cycle of the order in USDT; null when its result is not computed.
    /// </summary>
    public double? NetAmount { get; init; }
}