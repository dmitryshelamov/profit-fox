namespace ProfitFox.Domain.ValueObjects.Graph;

/// <summary>
///     A value read from a graph, or every error that stopped it.
/// </summary>
public sealed class ReadResult<T>
    where T : class
{
    public T? Value { get; }

    public IReadOnlyList<GraphError> Errors { get; }

    public bool IsValid => Value is not null;

    private ReadResult(T? value, IReadOnlyList<GraphError> errors)
    {
        Value = value;
        Errors = errors;
    }

    public static ReadResult<T> Valid(T value)
    {
        return new ReadResult<T>(value, []);
    }

    public static ReadResult<T> Invalid(IReadOnlyList<GraphError> errors)
    {
        if (errors.Count == 0)
        {
            throw new ArgumentException("An invalid result needs at least one error.", nameof(errors));
        }

        return new ReadResult<T>(value: null, errors);
    }
}