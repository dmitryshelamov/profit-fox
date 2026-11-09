namespace ProfitFox.Domain.ValueObjects.IdeaTrades;

/// <summary>The part of an execution of the exchange that closes a trade: its quantity, value and fee in the quote asset.</summary>
public sealed record ForcedFillShare(Guid TradeId, LegTotals Totals);