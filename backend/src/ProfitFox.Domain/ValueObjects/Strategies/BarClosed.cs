namespace ProfitFox.Domain.ValueObjects.Strategies;

public sealed record BarClosed(DateTimeOffset At, Bar Bar) : StrategyEvent(At);