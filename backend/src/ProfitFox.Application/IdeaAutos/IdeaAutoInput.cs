namespace ProfitFox.Application.IdeaAutos;

/// <summary>
///     The fields of an auto as the API gets them: the codes of the setups, types and intervals marked, the side, the market and the badge or none, and the
///     budget of the auto, none for no budget, the codes of the conditions by the features, none for «любой», and the two limits of the auto, none for
///     no limit.
/// </summary>
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
    int MinCases,
    decimal? Budget = null,
    string? DayDrop = null,
    string? BtcDrop = null,
    string? HighFunding = null,
    string? Burst = null,
    int? MaxPerMinutePerSide = null,
    int? MaxOpenPerSide = null);