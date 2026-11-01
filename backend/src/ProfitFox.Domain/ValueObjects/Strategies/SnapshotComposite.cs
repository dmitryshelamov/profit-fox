namespace ProfitFox.Domain.ValueObjects.Strategies;

/// <summary>A composite node as a strategy version holds it.</summary>
public sealed record SnapshotComposite(CompositeNodeId Id, CompositeDefinition Definition);