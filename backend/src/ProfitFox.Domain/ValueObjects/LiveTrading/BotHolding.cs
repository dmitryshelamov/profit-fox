using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Domain.ValueObjects.LiveTrading;

/// <summary>What one bot holds by its books: the quantity of its current position and the numbers of its active orders.</summary>
public sealed record BotHolding(Guid BotId, StrategyDirection Direction, decimal Quantity, IReadOnlyCollection<int> ActiveOrders);