using ProfitFox.Domain.Entities.SavedFilters.Enums;

namespace ProfitFox.Domain.ValueObjects.SavedFilters;

/// <summary>
///     A condition of a saved filter: a field, an operator and its value; «между» also has an upper bound, «надёжно > 0» no value, and a
///     field of choices (category, setup, type, interval, side) its <see cref="Choice" /> instead of a number.
/// </summary>
public sealed record FilterCondition(FilterField Field, FilterOperator Operator, decimal? Value = null, decimal? To = null, string? Choice = null);