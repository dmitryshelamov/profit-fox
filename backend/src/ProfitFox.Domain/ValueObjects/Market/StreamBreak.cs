namespace ProfitFox.Domain.ValueObjects.Market;

/// <summary>A break of a stream whose missed events cannot be loaded again: the name of the stream, when it was lost and when it was back.</summary>
public sealed record StreamBreak(string Stream, DateTimeOffset LostAt, DateTimeOffset RestoredAt);