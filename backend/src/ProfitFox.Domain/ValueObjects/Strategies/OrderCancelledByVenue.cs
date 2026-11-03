namespace ProfitFox.Domain.ValueObjects.Strategies;

/// <summary>The order was cancelled on the venue without a request of the bot, such as by the owner; the order is finished even when partly filled.</summary>
public sealed record OrderCancelledByVenue(DateTimeOffset At, string OrderId) : StrategyEvent(At);