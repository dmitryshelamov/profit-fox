using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Exceptions;

/// <summary>A composite node cannot be changed or removed because graphs that use it would break.</summary>
public sealed class NodeInUseException(IReadOnlyList<NodeUsage> usages) : Exception("The composite node is in use.")
{
    public IReadOnlyList<NodeUsage> Usages { get; } = usages;
}