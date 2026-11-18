namespace ProfitFox.Engine.Strategy.Graph;

/// <summary>
///     A named pin with its type: an input or output of a node, or a declared output of a composite node.
/// </summary>
public sealed record PinDeclaration(string Name, PinType Type);