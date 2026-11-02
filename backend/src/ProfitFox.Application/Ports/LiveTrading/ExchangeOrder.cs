namespace ProfitFox.Application.Ports.LiveTrading;

/// <summary>An order as the exchange knows it; <see cref="ClientId" /> is the id the bot gave it.</summary>
public sealed record ExchangeOrder(string ClientId, string ExchangeId, ExchangeOrderState State, decimal FilledQuantity, string? RejectReason);