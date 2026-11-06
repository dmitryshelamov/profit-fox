namespace ProfitFox.Domain.ValueObjects.IdeaTrades;

/// <summary>The trades that carry an expectancy of their promise: how many, the mean expectancy they were promised and the mean result in R they got.</summary>
public sealed record PromiseComparison(int Count, decimal? Promised, decimal? Got);