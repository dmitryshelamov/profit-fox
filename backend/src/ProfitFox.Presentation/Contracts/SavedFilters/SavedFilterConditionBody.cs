namespace ProfitFox.Presentation.Contracts.SavedFilters;

/// <summary>
///     A condition: a field code of the catalog, an operator (<c>&gt;</c>, <c>&gt;=</c>, <c>&lt;</c>, <c>&lt;=</c>, <c>=</c>, <c>between</c> or
///     <c>above_baseline</c>), its value and for <c>between</c> its upper bound, as decimal strings.
/// </summary>
public sealed record SavedFilterConditionBody(string? Field, string? Operator, string? Value, string? To);