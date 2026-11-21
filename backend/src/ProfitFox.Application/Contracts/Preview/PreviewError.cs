using ProfitFox.Domain.ValueObjects.Graph;

namespace ProfitFox.Application.Contracts.Preview;

/// <summary>
///     A problem with a preview query (spec signal-preview «Ошибки запроса»): an error of the graph reader as it is, or a
///     problem with the pair or the timeframe and its <see cref="Field" />.
/// </summary>
public sealed record PreviewError(string Code, string Reason)
{
    public string? Field { get; init; }

    /// <summary>
    ///     The error of the graph reader this problem is, with its place in the graph.
    /// </summary>
    public GraphError? Graph { get; init; }

    public static PreviewError Of(GraphError error)
    {
        return new PreviewError(error.Code.ToString(), error.Reason) { Field = error.Field, Graph = error };
    }
}
