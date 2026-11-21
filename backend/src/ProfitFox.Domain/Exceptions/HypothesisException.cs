namespace ProfitFox.Domain.Exceptions;

/// <summary>
///     A hypothesis text that cannot be run, with every problem found in it. The <c>subject</c> is its id when the id is
///     valid, or a label of the source.
/// </summary>
public class HypothesisException(string subject, IReadOnlyList<string> errors)
    : Exception($"{subject}: {string.Join("; ", errors)}")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
