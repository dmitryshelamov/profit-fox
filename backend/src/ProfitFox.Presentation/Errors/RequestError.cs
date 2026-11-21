namespace ProfitFox.Presentation.Errors;

/// <summary>
///     A problem with a request and where it is (spec signal-preview «Ошибки запроса»): a graph error keeps the code and
///     the place of the graph reader; a problem with the pair or the timeframe names its <see cref="Field" />.
/// </summary>
public sealed record RequestError(string Code, string Reason)
{
    public string? Field { get; init; }

    public string? Parameter { get; init; }

    public string? Node { get; init; }

    public string? Pin { get; init; }

    public string? Setting { get; init; }

    public RequestWire? Wire { get; init; }

    /// <summary>
    ///     The nodes of a cycle, in the order of its wires.
    /// </summary>
    public IReadOnlyList<string>? Nodes { get; init; }
}
