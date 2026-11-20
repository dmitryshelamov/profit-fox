using System.Text.Json;
using ProfitFox.Bench.Domain.Signals;
using ProfitFox.Engine.Strategy.Graph;

namespace ProfitFox.Bench.Presentation.Signals;

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
