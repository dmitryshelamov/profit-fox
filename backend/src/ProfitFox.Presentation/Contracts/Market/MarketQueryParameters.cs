namespace ProfitFox.Presentation.Contracts.Market;

/// <summary>Filters, order and page of the market snapshot; a filter left out takes every pair.</summary>
public sealed class MarketQueryParameters
{
    /// <summary><c>linear</c> or <c>spot</c>.</summary>
    public string? Category { get; init; }

    /// <summary>A part of the symbol, without regard to case.</summary>
    public string? Search { get; init; }

    /// <summary>A setup the pair holds on the interval of the setup.</summary>
    public string? Setup { get; init; }

    /// <summary>The interval of the setup, 5, 15, 60 or 240; 15 by default.</summary>
    public string? Interval { get; init; }

    /// <summary>The id of a saved filter the pairs must meet.</summary>
    public Guid? SavedFilter { get; init; }

    /// <summary>The column to order by (<c>symbol</c>, <c>price</c>, <c>change5m</c>, <c>change15m</c>, <c>change1h</c>, <c>change4h</c>, <c>change24h</c>, <c>turnover1h</c>, <c>turnover24h</c>, <c>openInterest</c>, <c>openInterestChange1h</c>, <c>openInterestChange24h</c>, <c>fundingRate</c> or <c>spread</c>); <c>change1h</c> by default.</summary>
    public string? Sort { get; init; }

    /// <summary><c>asc</c> or <c>desc</c>, <c>desc</c> by default.</summary>
    public string? Direction { get; init; }

    /// <summary>The number of the page from 1.</summary>
    public int? Page { get; init; }

    /// <summary>50, 100 or 200.</summary>
    public int? PageSize { get; init; }
}