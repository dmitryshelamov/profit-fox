namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>
///     An own idea from the price now: its levels, R:R of the prices, <see cref="NetRiskReward" /> after the fees and <see cref="BreakEven" />,
///     the share of targets it breaks even at after the fees if it never expires, none when the target does not cover them; <see cref="Made" />
///     false and no levels when the rules of its type make none. <see cref="Tick" /> is the price step of the pair. Numbers are decimal strings.
/// </summary>
public sealed record OwnIdeaResponse(bool Made, string? Entry, string? Stop, string? Target, string? RiskReward, string? NetRiskReward, string? BreakEven, string? Tick);