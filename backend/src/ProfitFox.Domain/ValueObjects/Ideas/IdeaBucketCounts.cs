namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>The cases of a bucket counted by outcome, with their mean R:R and their total in R.</summary>
public sealed record IdeaBucketCounts(IdeaBucketKey Key, int Targets, int Stops, int Expired, decimal AverageRiskReward, decimal TotalR);