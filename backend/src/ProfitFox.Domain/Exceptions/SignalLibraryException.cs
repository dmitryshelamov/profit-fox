using ProfitFox.Domain.ValueObjects.Graph;

namespace ProfitFox.Domain.Exceptions;

/// <summary>
///     A request the signal library refused, with every error of the name and the graph when it is invalid.
/// </summary>
public sealed class SignalLibraryException(SignalProblem problem, string message, IReadOnlyList<GraphError>? errors = null)
    : Exception(message)
{
    public SignalProblem Problem { get; } = problem;

    public IReadOnlyList<GraphError> Errors { get; } = errors ?? [];
}
