using System.Text.Json;
using ProfitFox.Domain.Signals;
using ProfitFox.Domain.Graph;

namespace ProfitFox.Presentation.Signals;

/// <summary>
///     A saved signal as the API sends it.
/// </summary>
public static class SignalResponses
{
    public static SignalResponse Of(SignalDocument signal)
    {
        using JsonDocument graph = JsonDocument.Parse(StrategyGraph.Write(signal.Graph));
        return new SignalResponse(signal.Name, signal.Description, signal.Version, graph.RootElement.Clone(), signal.Layout);
    }
}
