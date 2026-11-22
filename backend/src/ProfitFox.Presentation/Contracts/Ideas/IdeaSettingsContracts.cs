namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>
///     Whether a strategy gives ideas and on which timeframes (spec strategy-ideas «Включение идей у стратегии»).
/// </summary>
public sealed record StrategyIdeasRequest(bool Enabled, IReadOnlyList<string>? Timeframes);

public sealed record StrategyIdeasResponse(bool Enabled, IReadOnlyList<string> Timeframes);

/// <summary>
///     The label of a signal (spec idea-labels «Метка сигнала»): whether it is on, its text and its timeframe.
/// </summary>
public sealed record SignalLabelRequest(bool Enabled, string? Text, string? Timeframe);

public sealed record SignalLabelResponse(bool Enabled, string Text, string Timeframe);
