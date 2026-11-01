using System.Text.Json.Nodes;

namespace ProfitFox.Presentation.Contracts.Strategies;

/// <param name="Ref">The built-in node code or the custom node id, as graphs refer to the node.</param>
/// <param name="Definition"></param>
public sealed record VersionNodeResponse(string Ref, JsonObject Definition);