using System.Text.Json;

namespace ProfitFox.Bench.Presentation.Signals;

/// <summary>
///     A graph to check as a signal, without any history.
/// </summary>
public sealed record ValidateGraphRequest(JsonElement? Graph);
