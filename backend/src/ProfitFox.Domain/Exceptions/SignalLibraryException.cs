using ProfitFox.Domain.ValueObjects.Graph;

namespace ProfitFox.Domain.Exceptions;

/// <summary>
///     A request the signal library refused, with every error of the name and the graph when it is invalid, and the
///     strategies that point at the signal when it is in use.
/// </summary>
public sealed class SignalLibraryException(
    SignalProblem problem,
    string message,
    IReadOnlyList<GraphError>? errors = null,
    IReadOnlyList<string>? strategies = null)
    : Exception(message)
{
    public SignalProblem Problem { get; } = problem;

    public IReadOnlyList<GraphError> Errors { get; } = errors ?? [];

    /// <summary>
    ///     The names of the strategies that point at the signal, in name order, for <see cref="SignalProblem.InUse" />.
    /// </summary>
    public IReadOnlyList<string> Strategies { get; } = strategies ?? [];
}
