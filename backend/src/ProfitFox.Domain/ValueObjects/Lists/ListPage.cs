namespace ProfitFox.Domain.ValueObjects.Lists;

/// <summary>
///     A page of a list: its rows, the number of rows of the whole list, the page and its size.
/// </summary>
public sealed record ListPage<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
