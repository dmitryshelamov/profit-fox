using ProfitFox.Domain.ValueObjects.Bots;

namespace ProfitFox.Domain.Exceptions;

/// <summary>A strategy cannot be changed or removed because of its bots; <see cref="Bots" /> lists them.</summary>
public sealed class BotsAffectedException(IReadOnlyList<AffectedBot> bots) : Exception("The strategy has bots that stand in the way.")
{
    public IReadOnlyList<AffectedBot> Bots { get; } = bots;
}