namespace ProfitFox.Application.SavedFilters;

/// <summary>A condition as the user sent it: field and operator codes and decimal strings.</summary>
public sealed record SavedFilterConditionInput(string? Field, string? Operator, string? Value, string? To);