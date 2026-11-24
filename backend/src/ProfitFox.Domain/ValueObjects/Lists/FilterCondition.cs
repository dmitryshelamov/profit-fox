namespace ProfitFox.Domain.ValueObjects.Lists;

/// <summary>
///     A condition of a saved filter (spec saved-filters «Условие фильтра»): a field of the list of fields, an operator —
///     <c>gt</c>, <c>ge</c>, <c>lt</c>, <c>le</c>, <c>eq</c> or <c>between</c> — and its value as written, with the second
///     bound of <c>between</c>.
/// </summary>
public sealed record FilterCondition(string Field, string Operator, string? Value, string? To = null);
