using System.ComponentModel.DataAnnotations;

namespace ProfitFox.Presentation.Contracts.IdeaTrades;

/// <summary>An entry by an idea; without an account the settings of trading by ideas give it; the amount in USDT is required.</summary>
public sealed class IdeaTradeBody
{
    [Required]
    public Guid IdeaId { get; init; }

    public Guid? AccountId { get; init; }

    [Required]
    public decimal Amount { get; init; }
}