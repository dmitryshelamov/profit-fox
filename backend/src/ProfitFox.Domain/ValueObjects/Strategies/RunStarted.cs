namespace ProfitFox.Domain.ValueObjects.Strategies;

public sealed record RunStarted(DateTimeOffset At) : StrategyEvent(At);