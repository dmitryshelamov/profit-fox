using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Mvc;

namespace ProfitFox.Presentation.Contracts.MarketData;

public sealed class InstrumentsQueryParameters
{
    [Required]
    public string Category { get; init; } = string.Empty;

    /// <summary>A part of the symbol or of a coin, in any case.</summary>
    [Required]
    [FromQuery(Name = "query")]
    public string Query { get; init; } = string.Empty;

    /// <summary>From 1 to 100; 20 when omitted.</summary>
    public int? Limit { get; init; }
}