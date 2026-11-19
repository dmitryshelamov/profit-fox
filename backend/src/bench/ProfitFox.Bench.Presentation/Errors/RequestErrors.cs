using ProfitFox.Bench.Application.Preview;
using ProfitFox.Engine.Strategy.Graph.Reading;

namespace ProfitFox.Bench.Presentation.Errors;

/// <summary>
///     The errors of the graph reader and of the preview as the API tells them.
/// </summary>
public static class RequestErrors
{
    public static RequestError Of(PreviewError error)
    {
        return error.Graph is { } graph ? Of(graph) : new RequestError(error.Code, error.Reason) { Field = error.Field };
    }

    public static RequestError Of(GraphError error)
    {
        return new RequestError(error.Code.ToString(), error.Reason)
        {
            Field = error.Field,
            Parameter = error.Parameter,
            Node = error.Node,
            Pin = error.Pin,
            Setting = error.Setting,
            Wire = error.Wire is { } wire ? new RequestWire(wire.From.ToString(), wire.To.ToString()) : null,
            Nodes = error.Nodes.Count > 0 ? error.Nodes : null
        };
    }
}
