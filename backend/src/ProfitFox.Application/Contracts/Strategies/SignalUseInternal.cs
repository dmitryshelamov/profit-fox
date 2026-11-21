using ProfitFox.Domain.ValueObjects.Graph;

namespace ProfitFox.Application.Contracts.Strategies;

/// <summary>
///     What the editor shows on a signal node (change add-strategy-constructor, design.md, decision 5): the node, the signal it
///     points at by id with its current name, the version of the node, the latest version of the signal and the parameters of
///     the version of the node. The name and the latest version are null when the signal is not saved, the parameters empty
///     when the version is not found.
/// </summary>
public sealed record SignalUseInternal(
    string Node,
    Guid Signal,
    string? Name,
    int Version,
    int? LatestVersion,
    IReadOnlyList<ParameterDefinition> Parameters);
