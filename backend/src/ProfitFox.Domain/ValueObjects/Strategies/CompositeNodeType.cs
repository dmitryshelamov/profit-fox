namespace ProfitFox.Domain.ValueObjects.Strategies;

/// <summary>A reference to a composite node of the library; edits of that node reach this usage.</summary>
public sealed record CompositeNodeType(CompositeNodeId Id) : NodeType;