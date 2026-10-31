using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Services.Strategies.Validation;

/// <summary>Definitions of the composite nodes a graph may refer to.</summary>
public sealed class CompositeCatalog(IReadOnlyDictionary<CompositeNodeId, CompositeDefinition> definitions)
{
    public static CompositeCatalog Empty { get; } = new CompositeCatalog(new Dictionary<CompositeNodeId, CompositeDefinition>());

    public CompositeDefinition? Find(CompositeNodeId id)
    {
        return definitions.GetValueOrDefault(id);
    }
}