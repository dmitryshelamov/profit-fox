namespace ProfitFox.Presentation.Contracts.Nodes;

/// <summary>
///     A kind of the node catalog as the constructor draws it (spec signal-constructor «Каталог узлов»). The pins of
///     <c>parameters</c> and <c>outputs</c> come from the graph: <see cref="PinsFromGraph" /> says from which part.
/// </summary>
public sealed record NodeKindResponse(
    string Kind,
    string Title,
    string Group,
    IReadOnlyList<PinResponse> Inputs,
    IReadOnlyList<PinResponse> Outputs,
    IReadOnlyList<SettingResponse> Settings,
    string? PinsFromGraph);
