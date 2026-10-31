using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace ProfitFox.Presentation.Contracts.Strategies;

/// <summary>A name and a JSON definition: a strategy or a composite node.</summary>
public sealed class DefinitionBody
{
    [Required]
    public string Name { get; init; } = string.Empty;

    [Required]
    public JsonElement? Definition { get; init; }
}