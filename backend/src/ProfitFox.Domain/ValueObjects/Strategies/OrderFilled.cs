using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Domain.ValueObjects.Strategies;

/// <summary>
///     A fill of an order, possibly partial; <paramref name="IsComplete" /> says the order is filled completely. The source of the
///     fill charges <paramref name="Fee" /> in <paramref name="FeeAsset" />; the run only accounts for it.
/// </summary>
public sealed record OrderFilled(
    DateTimeOffset At,
    string OrderId,
    decimal Quantity,
    decimal Price,
    bool IsComplete,
    decimal Fee = 0m,
    FeeAsset FeeAsset = FeeAsset.Quote) : StrategyEvent(At);