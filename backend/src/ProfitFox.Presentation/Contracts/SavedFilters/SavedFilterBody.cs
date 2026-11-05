namespace ProfitFox.Presentation.Contracts.SavedFilters;

/// <summary>A saved filter: its name, where it is shown (<c>market</c>, <c>ideas</c> or <c>both</c>) and its conditions joined by «и».</summary>
public sealed class SavedFilterBody
{
    public string? Name { get; init; }

    public string? Place { get; init; }

    public IReadOnlyList<SavedFilterConditionBody>? Conditions { get; init; }
}