using System.ComponentModel.DataAnnotations;

namespace ProfitFox.Presentation.Contracts.IdeaTrades;

/// <summary>An entry by an idea; without an account or a risk the settings of trading by ideas give them.</summary>
public sealed class IdeaTradeBody
{
    [Required]
    public Guid IdeaId { get; init; }

    public Guid? AccountId { get; init; }

    public decimal? Risk { get; init; }
}