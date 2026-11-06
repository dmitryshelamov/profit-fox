namespace ProfitFox.Application.IdeaAutos;

/// <summary>The fields of an auto as the API gets them: the codes of the setups, types and intervals marked, the side, the market and the badge or none.</summary>
public sealed record IdeaAutoInput(
    string Name,
    Guid AccountId,
    decimal Amount,
    IReadOnlyList<string> Setups,
    IReadOnlyList<string> Types,
    IReadOnlyList<string> Intervals,
    string? Side,
    string? Category,
    string? Badge,
    decimal MinRiskReward,
    int MinCases);