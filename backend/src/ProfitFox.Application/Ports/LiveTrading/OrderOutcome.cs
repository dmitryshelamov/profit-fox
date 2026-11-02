namespace ProfitFox.Application.Ports.LiveTrading;

/// <summary>What the exchange answered to an order or a cancel; <see cref="Reason" /> is its message for a refusal.</summary>
public sealed record OrderOutcome(OrderOutcomeKind Kind, string? ExchangeId = null, string? Reason = null);