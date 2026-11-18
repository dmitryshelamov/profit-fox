namespace ProfitFox.Engine.Strategy.Graph;

/// <summary>
///     What reading a definition gave: the value when it is valid, every error found otherwise.
/// </summary>
public sealed class ReadResult<T>
    where T : class
{
    /// <summary>
    ///     The value read; null when there are errors.
    /// </summary>
    public T? Value { get; }

    /// <summary>
    ///     Every error found; empty when the value is valid.
    /// </summary>
    public IReadOnlyList<GraphError> Errors { get; }

    private ReadResult(T? value, IReadOnlyList<GraphError> errors)
    {
        Value = value;
        Errors = errors;
    }

    /// <summary>
    ///     A valid value.
    /// </summary>
    public static ReadResult<T> Valid(T value)
    {
        return new ReadResult<T>(value, []);
    }

    /// <summary>
    ///     A rejected definition with its errors.
    /// </summary>
    public static ReadResult<T> Invalid(IReadOnlyList<GraphError> errors)
    {
        return new ReadResult<T>(value: null, errors);
    }
}