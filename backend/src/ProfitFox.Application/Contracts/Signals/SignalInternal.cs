using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Signals;

namespace ProfitFox.Application.Contracts.Signals;

/// <summary>
///     A saved signal as the API shows it: its name, description, the graph in the format of spec strategy-graph, the positions
///     of its nodes by id and the number of its latest version (spec signal-constructor «Версии сигнала»), and its id, which a
///     strategy points at (spec signal-constructor «Идентификатор сигнала»).
/// </summary>
public sealed record SignalInternal(
    Guid Id,
    string Name,
    string? Description,
    GraphDefinition Graph,
    IReadOnlyDictionary<string, NodePosition> Layout,
    int Version);
