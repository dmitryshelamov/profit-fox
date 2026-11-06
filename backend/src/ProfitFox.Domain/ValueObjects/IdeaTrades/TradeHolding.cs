using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Domain.ValueObjects.IdeaTrades;

/// <summary>What one trade by an idea holds by its books: the quantity of its position and the numbers of its active orders.</summary>
public sealed record TradeHolding(Guid TradeId, StrategyDirection Direction, decimal Quantity, IReadOnlyCollection<int> ActiveOrders);