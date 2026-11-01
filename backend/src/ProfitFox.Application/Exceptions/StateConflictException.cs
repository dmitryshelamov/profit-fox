using ProfitFox.Domain.ValueObjects.Bots;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.Exceptions;

/// <summary>
///     The request conflicts with the current state; <see cref="Usages" /> lists the graphs and <see cref="Bots" /> the bots that
///     stand in the way, if any.
/// </summary>
public sealed class StateConflictException(string message, IReadOnlyList<NodeUsage> usages, IReadOnlyList<AffectedBot>? bots = null)
    : BaseApplicationException(message)
{
    public IReadOnlyList<NodeUsage> Usages { get; } = usages;

    public IReadOnlyList<AffectedBot>? Bots { get; } = bots;
}