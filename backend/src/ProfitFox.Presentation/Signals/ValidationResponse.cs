using ProfitFox.Presentation.Errors;

namespace ProfitFox.Presentation.Signals;

/// <summary>
///     Every error of a checked graph with its place; empty when the graph reads as a signal.
/// </summary>
public sealed record ValidationResponse(IReadOnlyList<RequestError> Errors);
