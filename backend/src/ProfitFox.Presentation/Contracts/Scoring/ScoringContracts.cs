namespace ProfitFox.Presentation.Contracts.Scoring;

/// <summary>
///     A row of the labels of «Скоринг» (change add-idea-score, spec idea-score «Раздел «Скоринг»»): the text, whether a label
///     with it is on, the timeframe and the signal of that label — none for the built-in label —, the weights for a long and
///     for a short, and whether it is the built-in label.
/// </summary>
public sealed record ScoringLabelResponse(string Text, bool Enabled, string? Timeframe, string? Signal, decimal LongWeight, decimal ShortWeight, bool BuiltIn);

/// <summary>
///     The weights of the label with the text, each a number from −100 to +100 with at most two digits after the point (change
///     decimal-label-weights, design.md, decision 2). Another number is refused at its field rather than when the body is read.
/// </summary>
public sealed record LabelWeightRequest(string? Text, decimal? LongWeight, decimal? ShortWeight);
