using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Domain.ValueObjects.Strategies;

public sealed record BuiltInNodeId(BuiltInNode Node) : CompositeNodeId;