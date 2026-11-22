namespace ProfitFox.Domain.ValueObjects.Lists;

/// <summary>
///     The page of a list asked for (change add-ideas-and-market, design.md, decision 10): the field to sort by, descending or
///     not, the page from 1 and its size of 50, 100 or 200 rows.
/// </summary>
public sealed record ListOrder(string Sort, bool Descending, int Page, int PageSize)
{
    public int Offset => (Page - 1) * PageSize;
}
