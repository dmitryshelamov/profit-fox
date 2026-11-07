using ProfitFox.Domain.Entities.IdeaAutos.Enums;
using ProfitFox.Domain.ValueObjects.IdeaAutos;

namespace ProfitFox.Domain.Entities.IdeaAutos;

/// <summary>
///     A named auto that takes the ideas that fit its conditions, on a paper or with the money of an account. Its place in the list is its priority;
///     a deleted auto stays for the names of its trades. <see cref="Budget" /> caps the sum of the amounts of its open trades, none while it has no budget.
///     <see cref="MaxPerMinutePerSide" /> caps the new trades of a side it takes in a minute and <see cref="MaxOpenPerSide" /> its open trades of a side,
///     real and paper, none without a limit (decision 3 of add-burst-and-auto-limits).
/// </summary>
public sealed record IdeaAuto(
    Guid Id,
    string Name,
    Guid AccountId,
    decimal Amount,
    IdeaAutoMode Mode,
    IdeaAutoConditions Conditions,
    int Position,
    DateTimeOffset? DeletedAt = null,
    decimal? Budget = null,
    int? MaxPerMinutePerSide = null,
    int? MaxOpenPerSide = null)
{
    public static IdeaAuto Create(Guid id, string name, Guid accountId, decimal amount, IdeaAutoConditions conditions, int position)
    {
        return new IdeaAuto(id, name, accountId, amount, IdeaAutoMode.Off, conditions, position);
    }
}