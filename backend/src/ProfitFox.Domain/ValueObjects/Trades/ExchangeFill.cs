using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Domain.ValueObjects.Trades;

/// <summary>
///     A fill of the account as the exchange tells it, by its stream or by a read (change take-idea-trades, design.md,
///     decision 3): the id of the execution, the pair, the link id of the order — empty for a fill without an order of the
///     account, such as a liquidation —, the side, the price, the quantity, the fee — for a payment of funding the funding
///     paid, negative when received —, the kind, the conditional order it came from, and when it happened.
/// </summary>
public sealed record ExchangeFill(
    string ExecId,
    string Symbol,
    string? OrderLinkId,
    TradeSide Side,
    decimal Price,
    decimal Quantity,
    decimal Fee,
    ExchangeFillType Type,
    ExchangeStopType StopType,
    DateTimeOffset Time) : ExchangeMessage(Symbol);
