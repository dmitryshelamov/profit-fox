namespace ProfitFox.Domain.ValueObjects.Strategies;

public sealed record CustomNodeId(Guid Value) : CompositeNodeId;