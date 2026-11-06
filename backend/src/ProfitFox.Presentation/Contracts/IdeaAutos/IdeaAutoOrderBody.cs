using System.ComponentModel.DataAnnotations;

namespace ProfitFox.Presentation.Contracts.IdeaAutos;

/// <summary>The ids of every auto of the list in the new order, the first the most important.</summary>
public sealed class IdeaAutoOrderBody
{
    [Required]
    public required IReadOnlyList<Guid> Ids { get; init; }
}