using ProfitFox.Domain.Entities.SavedFilters.Enums;

namespace ProfitFox.Domain.ValueObjects.SavedFilters;

/// <summary>A condition of a saved filter: a field, an operator and its value; «между» also has an upper bound, «выше базовой» no value.</summary>
public sealed record FilterCondition(FilterField Field, FilterOperator Operator, decimal? Value = null, decimal? To = null);