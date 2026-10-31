using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Entities.Strategies;

/// <summary>A custom composite node of the library.</summary>
public sealed record CompositeNode(Guid Id, string Name, CompositeDefinition Definition, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);