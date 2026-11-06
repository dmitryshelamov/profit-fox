namespace ProfitFox.Domain.ValueObjects.IdeaTrades;

/// <summary>The trades whose idea has a result in R: how many, the mean result of their ideas and the mean result of the trades, both in R.</summary>
public sealed record IdeaComparison(int Count, decimal? Idea, decimal? Trades);