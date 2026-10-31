namespace ProfitFox.Domain.ValueObjects.Strategies;

/// <summary>Something the strategy reacts to; <see cref="At" /> is UTC.</summary>
public abstract record StrategyEvent(DateTimeOffset At);