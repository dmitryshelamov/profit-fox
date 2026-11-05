using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Domain.Entities.IdeaTrades;

/// <summary>One execution of an order of a trade, its entry, its close, its stop or its target; <see cref="ExecId" /> is the exchange's id of it.</summary>
public sealed record IdeaTradeFill(string ExecId, Guid TradeId, string ExchangeOrderId, decimal Quantity, decimal Price, decimal Fee, FeeAsset FeeAsset, DateTimeOffset ExecutedAt);