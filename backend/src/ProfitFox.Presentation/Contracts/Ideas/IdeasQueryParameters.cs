namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>
///     Filters, order and page of the list of ideas; a filter left out takes every idea, no column the expectancy, highest first, no page the
///     first of 50.
/// </summary>
public sealed class IdeasQueryParameters
{
    public string? Setup { get; init; }

    public string? Interval { get; init; }

    public string? Type { get; init; }

    public string? Side { get; init; }

    public string? Category { get; init; }

    /// <summary>Only ideas whose bucket expects at least 0 R.</summary>
    public bool NonNegativeExpectancy { get; init; }

    /// <summary>Only ideas awaiting entry and active ones: none that reached the target, the stop or expired.</summary>
    public bool ActiveOnly { get; init; }

    /// <summary>No ideas of non-crypto pairs: stocks (spot xStocks too), ETFs, commodities and forex.</summary>
    public bool HideStocks { get; init; }

    /// <summary>The id of a saved filter the ideas must meet.</summary>
    public Guid? SavedFilter { get; init; }

    /// <summary>The column of the list to order by (<c>firedAt</c>, <c>symbol</c>, <c>setup</c>, <c>interval</c>, <c>type</c>, <c>side</c>, <c>entry</c>, <c>stop</c>, <c>target</c>, <c>riskReward</c>, <c>probability</c>, <c>expectancy</c>, <c>profit</c> or <c>status</c>).</summary>
    public string? Sort { get; init; }

    /// <summary><c>asc</c> or <c>desc</c>, <c>desc</c> by default.</summary>
    public string? Direction { get; init; }

    /// <summary>The number of the page from 1.</summary>
    public int? Page { get; init; }

    /// <summary>50, 100 or 200.</summary>
    public int? PageSize { get; init; }
}