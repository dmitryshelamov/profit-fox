namespace ProfitFox.Application.Queries.Filters;

/// <summary>
///     The common part of a request of a list (change add-ideas-and-market, design.md, decision 10): the field and direction
///     of sorting, the page from 1, its size and the saved filter.
/// </summary>
public sealed record ListRequest(string? Sort = null, string? Direction = null, int? Page = null, int? PageSize = null, Guid? Filter = null);
