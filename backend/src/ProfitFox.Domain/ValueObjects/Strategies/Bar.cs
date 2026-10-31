namespace ProfitFox.Domain.ValueObjects.Strategies;

/// <summary>A closed candle as the strategy sees it.</summary>
public sealed record Bar(decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);