using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Ideas.Queries.GetOwnIdea;

/// <summary>
///     The levels of the idea and its baseline, or no levels when the rules of the type make none (no zone it needs, a reward under 1.5 risks,
///     too little history, no price step of the pair yet), the price step <see cref="Tick" /> of the pair and the reward to risk after the fees <see cref="NetRiskReward" />; <see cref="NotFound" /> when the snapshot has no such pair; or validation errors.
/// </summary>
public sealed record GetOwnIdeaResult(IdeaPlan? Plan, bool NotFound, IReadOnlyDictionary<string, string> Errors, decimal? Tick = null, decimal? NetRiskReward = null)
{
    public decimal? Baseline => Plan is null ? null : 1m / (1m + Plan.RiskReward);
}