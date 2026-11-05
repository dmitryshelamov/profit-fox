using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.IdeaTrades;

namespace ProfitFox.Application.Ports.LiveTrading;

/// <summary>
///     An order for the exchange: a limit order has a price, a market buy on the spot may be sized by <see cref="QuoteAmount" />
///     instead of a quantity. An entry of a trade by an idea carries its <see cref="Protection" />, which the exchange stands as a stop and
///     a target; a close of a trade only reduces the position (<see cref="ReduceOnly" />).
/// </summary>
public sealed record OrderRequest(
    string ClientId,
    OrderSide Side,
    OrderType Type,
    decimal? Price,
    decimal? Quantity,
    decimal? QuoteAmount,
    Protection? Protection = null,
    TimeInForce TimeInForce = TimeInForce.GoodTilCancelled,
    bool ReduceOnly = false);