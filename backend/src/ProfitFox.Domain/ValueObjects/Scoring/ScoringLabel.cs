namespace ProfitFox.Domain.ValueObjects.Scoring;

/// <summary>
///     A row of the labels of «Скоринг» (change add-idea-score, spec idea-score «Раздел «Скоринг»»; design.md, decision 3):
///     a text of the labels of signals, whether a label with it is on, the timeframe and the name of the signal of the label
///     that is on — or of the first one by the name of its signal when none is —, and its weights. The built-in label has no
///     timeframe and no signal. The weights are numbers with at most two digits after the point (change
///     decimal-label-weights).
/// </summary>
public sealed record ScoringLabel(string Text, bool Enabled, string? Timeframe, string? Signal, decimal LongWeight, decimal ShortWeight, bool BuiltIn = false);
