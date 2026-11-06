using System.ComponentModel.DataAnnotations;

namespace ProfitFox.Presentation.Contracts.IdeaAutos;

/// <summary>
///     An auto to save: its name, account and amount in USDT, and its conditions: the codes of the setups, types and intervals marked (the types never hold
///     <c>stop_entry</c>), the side (<c>long</c> or <c>short</c>, none is both), the market (<c>linear</c> or <c>spot</c>, none is both), the lowest badge
///     (<c>up</c>, <c>flat</c> or <c>down</c>, none is any), the lowest reward to the risk after the fees and the least number of cases of the bucket.
/// </summary>
public sealed class IdeaAutoBody
{
    [Required]
    public required string Name { get; init; }

    public Guid AccountId { get; init; }

    public decimal Amount { get; init; }

    [Required]
    public required IReadOnlyList<string> Setups { get; init; }

    [Required]
    public required IReadOnlyList<string> Types { get; init; }

    [Required]
    public required IReadOnlyList<string> Intervals { get; init; }

    public string? Side { get; init; }

    public string? Category { get; init; }

    public string? Badge { get; init; }

    public decimal MinRiskReward { get; init; }

    public int MinCases { get; init; }
}