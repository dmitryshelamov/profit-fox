using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Exceptions;

/// <summary>A strategy or a composite node fails validation; <see cref="Errors" /> lists every problem.</summary>
public sealed class GraphValidationException(IReadOnlyList<GraphError> errors) : Exception("The graph is invalid.")
{
    public IReadOnlyList<GraphError> Errors { get; } = errors;
}