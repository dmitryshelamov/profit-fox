namespace ProfitFox.Domain.ValueObjects.IdeaTrades;

/// <summary>A trade that holds a position an execution of the exchange closes, and the quantity it holds.</summary>
public sealed record ForcedFillHolder(Guid TradeId, decimal Held);