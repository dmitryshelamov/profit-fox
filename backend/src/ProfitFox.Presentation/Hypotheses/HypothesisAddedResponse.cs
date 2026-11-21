namespace ProfitFox.Presentation.Hypotheses;

/// <summary>
///     The answer to a loaded text: the hypothesis and the version its text has.
/// </summary>
public sealed record HypothesisAddedResponse(string Id, string Name, int Version);
