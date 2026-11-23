namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     What the scanner runs as one answer of the database (change add-ideas-and-market, task 18.13): the enabled strategies
///     with their names, latest version numbers and timeframes, and the enabled labels with the names and latest version
///     numbers of their signals. The graphs are read only when a version is new.
/// </summary>
public sealed record IdeaSources(IReadOnlyList<StrategySource> Strategies, IReadOnlyList<LabelSource> Labels);

/// <summary>
///     An enabled strategy with at least one timeframe: its name, latest version and timeframes.
/// </summary>
public sealed record StrategySource(Guid StrategyId, string Name, int Version, IReadOnlyList<TimeSpan> Timeframes);

/// <summary>
///     An enabled label: the name and latest version of its signal, its text and timeframe.
/// </summary>
public sealed record LabelSource(Guid SignalId, string Name, int Version, string Text, TimeSpan Timeframe);
