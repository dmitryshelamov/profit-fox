namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>
///     An own idea from the price now: its levels, R:R and the baseline 1 / (1 + R:R), its only probability («без сетапа: только базовая
///     линия»); <see cref="Made" /> false and no levels when the rules of its type make none. Numbers are decimal strings.
/// </summary>
public sealed record OwnIdeaResponse(bool Made, string? Entry, string? Stop, string? Target, string? RiskReward, string? Baseline);