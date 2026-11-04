namespace ProfitFox.Application.SavedFilters;

/// <summary>A saved filter as the user sent it: its name, place code and conditions.</summary>
public sealed record SavedFilterInput(string? Name, string? Place, IReadOnlyList<SavedFilterConditionInput>? Conditions);