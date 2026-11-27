namespace ProfitFox.Presentation.Contracts.Filters;

/// <summary>
///     A condition of a filter as the API takes and gives it (spec saved-filters «Условие фильтра»): a field, an operator —
///     <c>gt</c>, <c>ge</c>, <c>lt</c>, <c>le</c>, <c>eq</c>, <c>between</c> — the value in the unit of the field and, for
///     <c>between</c>, the second bound.
/// </summary>
public sealed record FilterConditionDto(string Field, string Operator, string? Value, string? To = null);

/// <summary>
///     A saved filter: its id, name, place (<c>market</c>, <c>ideas</c>, <c>both</c>) and conditions.
/// </summary>
public sealed record FilterResponse(Guid Id, string Name, string Place, IReadOnlyList<FilterConditionDto> Conditions);

/// <summary>
///     What a filter is saved with.
/// </summary>
public sealed record SaveFilterRequest(string? Name, string? Place, IReadOnlyList<FilterConditionDto>? Conditions);

/// <summary>
///     A field of a filter (spec saved-filters «Поля фильтра»): its name, title, type (<c>number</c>, <c>text</c>,
///     <c>choice</c>, <c>boolean</c>), whether it is of the market or of an idea, its unit and its values.
/// </summary>
public sealed record FilterFieldResponse(string Name, string Title, string Type, string Place, string? Unit, IReadOnlyList<string>? Values);

/// <summary>
///     The conditions a count is asked for, not saved yet.
/// </summary>
public sealed record FilterCountRequest(IReadOnlyList<FilterConditionDto>? Conditions);

/// <summary>
///     How many pairs and open ideas match, of all.
/// </summary>
public sealed record FilterCountResponse(int Pairs, int AllPairs, int Ideas, int AllIdeas);
