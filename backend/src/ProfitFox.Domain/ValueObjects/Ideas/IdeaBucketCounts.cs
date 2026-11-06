namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>The cases of a bucket counted by outcome, with their total in R and the total of the squares of their results.</summary>
public sealed record IdeaBucketCounts(IdeaBucketKey Key, int Targets, int Stops, int Expired, decimal TotalR, decimal TotalSquaredR);