namespace ProfitFox.Application.Commands.Hypotheses.AddHypothesis;

/// <summary>
///     The answer to a loaded text (spec hypothesis-bench «Заморозка гипотезы»): the hypothesis with the version of the
///     text, and whether the text made that version or was already it.
/// </summary>
public sealed record AddHypothesisResult(string Id, string Name, int Version, bool Created);
