using ProfitFox.Domain.Entities.IdeaTrades.Enums;

namespace ProfitFox.Domain.Entities.IdeaTrades;

/// <summary>
///     The stop or the target the exchange made for the entry of a trade, known by the exchange's id of it. It carries the totals the
///     exchange counted of it, in the quote asset, and whether it is over.
/// </summary>
public sealed record IdeaTradeChild(Guid TradeId, string ExchangeOrderId, IdeaTradeLeg Leg, decimal FilledQuantity, decimal FilledValue, decimal Fee, bool Finished);