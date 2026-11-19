using System.Text.Json;
using ProfitFox.Bench.Domain.Signals;

namespace ProfitFox.Bench.Presentation.Signals;

/// <summary>
///     A saved signal: its graph in the format of spec strategy-graph and, apart from it, the positions of its nodes.
/// </summary>
public sealed record SignalResponse(string Name, string? Description, JsonElement Graph, IReadOnlyDictionary<string, NodePosition> Layout);
