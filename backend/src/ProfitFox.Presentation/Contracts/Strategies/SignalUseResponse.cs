namespace ProfitFox.Presentation.Contracts.Strategies;

/// <summary>
///     What a signal node of a strategy points at (change add-strategy-constructor, design.md, decision 5): the node, the id
///     and the current name of the signal, the version of the node, the latest version of the signal and the parameters of the
///     version of the node. The name and the latest version are null when the signal is not saved; the parameters are empty
///     when the version is not found.
/// </summary>
public sealed record SignalUseResponse(
    string Node,
    Guid Signal,
    string? Name,
    int Version,
    int? LatestVersion,
    IReadOnlyList<SignalParameterResponse> Parameters);
