namespace ProfitFox.Presentation.Contracts.Backtests;

public sealed class ResultsQueryParameters
{
    /// <summary>A metric such as <c>return</c> or <c>winRate</c>, or <c>combination</c>, the default.</summary>
    public string? Sort { get; init; }

    /// <summary><c>asc</c>, the default, or <c>desc</c>.</summary>
    public string? Order { get; init; }

    public int? Offset { get; init; }

    /// <summary>From 1 to 1000, 100 by default.</summary>
    public int? Limit { get; init; }
}