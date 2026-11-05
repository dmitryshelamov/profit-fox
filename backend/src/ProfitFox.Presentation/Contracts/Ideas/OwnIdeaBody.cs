using System.ComponentModel.DataAnnotations;

namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>The type, interval and side of an own idea: codes as in the ideas.</summary>
public sealed class OwnIdeaBody
{
    [Required]
    public string? Type { get; init; }

    [Required]
    public string? Interval { get; init; }

    [Required]
    public string? Side { get; init; }
}