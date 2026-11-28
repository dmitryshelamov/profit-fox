using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Domain.ValueObjects.Trades;

/// <summary>
///     The count of an entry (change take-idea-trades, design.md, decision 7; spec idea-trades «Расчёт перед входом»): the side,
///     the price now, the amount and the leverage, the quantity on the step of the pair, the value of the position and its
///     margin; the entry, stop and target of the idea; the stop of the trade, where it comes from and the price at the limit,
///     the target; their distances from the price now as signed fractions; the loss at the stop and the profit at the target
///     with the fees, in USDT; the estimated price of the liquidation; the free money needed; the end; and every check in
///     its order. The stop is null only when neither the idea nor the limit gives one.
/// </summary>
public sealed record EntryCalculation(
    TradeSide Side,
    decimal Price,
    decimal Amount,
    int Leverage,
    decimal Quantity,
    decimal Value,
    decimal Margin,
    decimal IdeaEntry,
    decimal? IdeaStop,
    decimal? IdeaTarget,
    decimal? Stop,
    StopSource? StopSource,
    decimal? LimitStop,
    decimal? StopDistance,
    decimal? Target,
    decimal? TargetDistance,
    decimal? LossAtStop,
    decimal? ProfitAtTarget,
    decimal Liquidation,
    decimal Needed,
    DateTimeOffset Until,
    IReadOnlyList<EntryCheck> Checks)
{
    public bool Passed => Checks.All(check => check.Passed);
}
