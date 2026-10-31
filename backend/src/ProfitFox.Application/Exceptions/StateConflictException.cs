using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.Exceptions;

/// <summary>The request conflicts with the current state; <see cref="Usages" /> lists the graphs that stand in the way, if any.</summary>
public sealed class StateConflictException(string message, IReadOnlyList<NodeUsage> usages) : BaseApplicationException(message)
{
    public IReadOnlyList<NodeUsage> Usages { get; } = usages;
}