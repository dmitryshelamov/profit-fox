using ProfitFox.Domain.ValueObjects.Graph.Enums;

namespace ProfitFox.Domain.ValueObjects.Nodes;

/// <summary>
///     An input or output a node kind declares.
/// </summary>
public sealed record PinDeclaration(string Name, PinType Type);