using ProfitFox.Domain.Graph.Model;

namespace ProfitFox.Domain.Nodes;

/// <summary>
///     An input or output a node kind declares.
/// </summary>
public sealed record PinDeclaration(string Name, PinType Type);