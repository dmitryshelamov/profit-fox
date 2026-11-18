using ProfitFox.Engine.Strategy.Graph.Model;

namespace ProfitFox.Engine.Strategy.Nodes;

/// <summary>
///     An input or output a node kind declares.
/// </summary>
public sealed record PinDeclaration(string Name, PinType Type);