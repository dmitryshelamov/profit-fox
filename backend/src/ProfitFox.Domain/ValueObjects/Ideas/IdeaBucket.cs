namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>The statistics of a bucket over the last <see cref="Days" /> days.</summary>
public sealed record IdeaBucket(IdeaBucketKey Key, int Days, BucketStatistics Statistics);