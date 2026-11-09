namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     How far the replay of history of a version of the rules has gone (decision 4 of speed-up-ideas-replay): of the <see cref="Total" /> pairs
///     the replay takes, <see cref="Done" /> have their cursor on the day of their coverage; <see cref="Slowest" /> is the earliest cursor of the
///     others, none when none of them has one.
/// </summary>
public sealed record IdeaReplayProgress(int Done, int Total, DateTimeOffset? Slowest, int EngineVersion);