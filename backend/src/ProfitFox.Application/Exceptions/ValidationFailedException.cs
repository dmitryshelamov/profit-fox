namespace ProfitFox.Application.Exceptions;

/// <summary>Request parameters are invalid; <see cref="Errors" /> are keyed by parameter name.</summary>
public sealed class ValidationFailedException(IReadOnlyDictionary<string, string> errors)
    : BaseApplicationException("Request parameters are invalid.")
{
    public IReadOnlyDictionary<string, string> Errors { get; } = errors;
}