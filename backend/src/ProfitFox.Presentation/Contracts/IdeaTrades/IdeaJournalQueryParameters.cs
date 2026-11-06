namespace ProfitFox.Presentation.Contracts.IdeaTrades;

/// <summary>Filters and page of the journal; a filter left out takes every trade, no page the first of 50.</summary>
public sealed class IdeaJournalQueryParameters
{
    public string? Setup { get; init; }

    /// <summary>A part of the symbol, in any case.</summary>
    public string? Symbol { get; init; }

    public string? Side { get; init; }

    /// <summary>The badge of the promise, <c>up</c>, <c>flat</c> or <c>down</c>, or <c>none</c> for a trade with no badge.</summary>
    public string? Badge { get; init; }

    /// <summary>The period of the time of the close, 7, 30 or 90 days; it does not touch the open trades.</summary>
    public int? Days { get; init; }

    /// <summary>The number of the page of the closed trades from 1.</summary>
    public int? Page { get; init; }

    /// <summary>50, 100 or 200.</summary>
    public int? PageSize { get; init; }
}