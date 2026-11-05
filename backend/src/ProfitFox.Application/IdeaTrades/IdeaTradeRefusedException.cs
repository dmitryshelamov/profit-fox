using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;

namespace ProfitFox.Application.IdeaTrades;

/// <summary>An entry by an idea that the checks stop: the reasons say what.</summary>
public sealed class IdeaTradeRefusedException(IReadOnlyList<IdeaTradeRefusal> reasons)
    : BaseApplicationException($"The entry is refused: {string.Join(", ", reasons.Select(IdeaTradeCodes.ToCode))}.")
{
    public IReadOnlyList<IdeaTradeRefusal> Reasons { get; } = reasons;
}