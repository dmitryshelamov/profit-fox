namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>Filters of the list of ideas; a filter left out takes every idea.</summary>
public sealed class IdeasQueryParameters
{
    public string? Setup { get; init; }

    public string? Interval { get; init; }

    public string? Type { get; init; }

    public string? Side { get; init; }

    public string? Category { get; init; }

    /// <summary>Only ideas whose bucket expects at least 0 R.</summary>
    public bool NonNegativeExpectancy { get; init; }
}