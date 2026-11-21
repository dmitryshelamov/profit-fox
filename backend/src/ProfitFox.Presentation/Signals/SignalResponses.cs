using System.Text.Json;

using ProfitFox.Domain.Graph;
using ProfitFox.Domain.Signals;

namespace ProfitFox.Presentation.Signals;

/// <summary>
///     A saved signal as the API sends it.
/// </summary>
public static class SignalResponses
{
    public static SignalResponse Of(SignalDocument signal)
    {
        using var graph = JsonDocument.Parse(StrategyGraph.Write(signal.Graph));
        return new SignalResponse(signal.Name, signal.Description, signal.Version, graph.RootElement.Clone(), signal.Layout);
    }
}
