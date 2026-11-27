using System.Text.Json;

using ProfitFox.Application.Contracts.Signals;
using ProfitFox.Domain.Services.Graph;

namespace ProfitFox.Presentation.Contracts.Signals.Converters;

/// <summary>
///     A saved signal as the API sends it.
/// </summary>
public static class SignalResponses
{
    public static SignalResponse Of(SignalInternal signal)
    {
        using var graph = JsonDocument.Parse(StrategyGraph.Write(signal.Graph));
        return new SignalResponse(signal.Name, signal.Description, signal.Version, graph.RootElement.Clone(), signal.Layout, signal.Id);
    }
}
