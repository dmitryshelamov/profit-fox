using System.ComponentModel.DataAnnotations;

namespace ProfitFox.Presentation.Contracts.IdeaAutos;

/// <summary>The mode of an auto: <c>off</c>, <c>paper</c> or <c>live</c>.</summary>
public sealed class IdeaAutoModeBody
{
    [Required]
    public required string Mode { get; init; }
}