using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Domain.ValueObjects.Strategies;

/// <summary>
///     An order of a strategy and what became of it (spec strategy-preview «Исход заявки»): its moment, its node — null when
///     the step did not name it — its side and its result, why it gave no trade when it did not, and for a fill the price,
///     the amount in USDT and the index of its cycle. <see cref="Trade" /> is the trade of a plan the order opened, for a
///     strategy by plan: what the bench counts.
/// </summary>
public sealed record OrderOutcome(
    DateTimeOffset Moment,
    string? Node,
    OrderSide Side,
    OrderResult Result,
    OrderExclusion? Exclusion,
    double? Price,
    double? Amount,
    int? Cycle)
{
    public TradeOutcome? Trade { get; init; }
}
