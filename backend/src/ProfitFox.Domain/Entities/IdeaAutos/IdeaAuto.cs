using ProfitFox.Domain.Entities.IdeaAutos.Enums;
using ProfitFox.Domain.ValueObjects.IdeaAutos;

namespace ProfitFox.Domain.Entities.IdeaAutos;

/// <summary>
///     A named auto that takes the ideas that fit its conditions, on a paper or with the money of an account. Its place in the list is its priority;
///     a deleted auto stays for the names of its trades.
/// </summary>
public sealed record IdeaAuto(Guid Id, string Name, Guid AccountId, decimal Amount, IdeaAutoMode Mode, IdeaAutoConditions Conditions, int Position, DateTimeOffset? DeletedAt = null)
{
    public static IdeaAuto Create(Guid id, string name, Guid accountId, decimal amount, IdeaAutoConditions conditions, int position)
    {
        return new IdeaAuto(id, name, accountId, amount, IdeaAutoMode.Off, conditions, position);
    }
}