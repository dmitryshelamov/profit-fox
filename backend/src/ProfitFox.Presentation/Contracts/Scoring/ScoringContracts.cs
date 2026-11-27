namespace ProfitFox.Presentation.Contracts.Scoring;

/// <summary>
///     A row of the labels of «Скоринг» (change add-idea-score, spec idea-score «Раздел «Скоринг»»): the text, whether a label
///     with it is on, the timeframe and the signal of that label — none for the built-in label —, the weights for a long and
///     for a short, and whether it is the built-in label.
/// </summary>
public sealed record ScoringLabelResponse(string Text, bool Enabled, string? Timeframe, string? Signal, int LongWeight, int ShortWeight, bool BuiltIn);

/// <summary>
///     The weights of the label with the text, each a whole number from −3 to +3. A number that is not whole is refused at
///     its field rather than when the body is read.
/// </summary>
public sealed record LabelWeightRequest(string? Text, decimal? LongWeight, decimal? ShortWeight);
