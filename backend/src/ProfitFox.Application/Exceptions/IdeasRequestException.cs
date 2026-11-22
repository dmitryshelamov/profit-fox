using ProfitFox.Application.Contracts.Preview;

namespace ProfitFox.Application.Exceptions;

/// <summary>
///     A request about ideas — the ideas of a strategy, the label of a signal, a saved filter or a list — was refused with
///     every error, each with its place: a field, a condition or a node of the graph (change add-ideas-and-market). Nothing
///     was written.
/// </summary>
public sealed class IdeasRequestException(IReadOnlyList<PreviewErrorInternal> errors)
    : Exception(string.Join("; ", errors.Select(error => error.Reason)))
{
    public IReadOnlyList<PreviewErrorInternal> Errors { get; } = errors;
}
