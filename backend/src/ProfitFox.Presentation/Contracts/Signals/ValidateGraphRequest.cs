using System.Text.Json;

namespace ProfitFox.Presentation.Contracts.Signals;

/// <summary>
///     A graph to check as a signal, without any history.
/// </summary>
public sealed record ValidateGraphRequest(JsonElement? Graph);
