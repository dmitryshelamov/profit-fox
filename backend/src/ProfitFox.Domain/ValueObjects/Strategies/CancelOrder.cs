namespace ProfitFox.Domain.ValueObjects.Strategies;

public sealed record CancelOrder(string OrderId) : OrderCommand(OrderId);