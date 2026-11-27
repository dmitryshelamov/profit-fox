using System.Text.Json;

using ProfitFox.Domain.ValueObjects.Signals;

namespace ProfitFox.Presentation.Contracts.Signals;

/// <summary>
///     A saved signal: the graph of its latest version in the format of spec strategy-graph and, apart from it, the positions
///     of its nodes, and its id, which stays through renames and new versions (spec signal-constructor «Идентификатор
///     сигнала»).
/// </summary>
public sealed record SignalResponse(
    string Name,
    string? Description,
    int Version,
    JsonElement Graph,
    IReadOnlyDictionary<string, NodePosition> Layout,
    Guid Id);
