namespace ProfitFox.Application.Ports.LiveTrading;

/// <summary>
///     What the exchange answered to an order or a cancel; <see cref="Reason" /> is its message for a refusal. A transient refusal may
///     carry <see cref="RetryAfter" />, how long another attempt is useless, such as until the next session opens.
/// </summary>
public sealed record OrderOutcome(OrderOutcomeKind Kind, string? ExchangeId = null, string? Reason = null, TimeSpan? RetryAfter = null);