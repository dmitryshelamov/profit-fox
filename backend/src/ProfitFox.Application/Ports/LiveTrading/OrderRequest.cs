using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Application.Ports.LiveTrading;

/// <summary>
///     An order for the exchange: a limit order has a price, a market buy on the spot may be sized by <see cref="QuoteAmount" />
///     instead of a quantity.
/// </summary>
public sealed record OrderRequest(string ClientId, OrderSide Side, OrderType Type, decimal? Price, decimal? Quantity, decimal? QuoteAmount);