namespace ProfitFox.Presentation.Contracts.SavedFilters;

/// <summary>The conditions of a draft of a filter, in the form of a saved filter; none fits every pair and every idea.</summary>
public sealed class SavedFilterFitBody
{
    public IReadOnlyList<SavedFilterConditionBody>? Conditions { get; init; }
}