namespace ProfitFox.Presentation.Errors;

/// <summary>
///     The answer to a refused request: every problem found, before any history is read.
/// </summary>
public sealed record ErrorResponse(IReadOnlyList<RequestError> Errors);
