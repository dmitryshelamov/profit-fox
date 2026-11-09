using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Repositories;

/// <summary>Stores the statistics of the buckets and the curves of «Уровни» by the reward to risk, which the scanner computes and the API reads.</summary>
public interface IIdeaBucketRepository
{
    /// <summary>
    ///     Replaces the buckets and the curves of the version with <paramref name="buckets" /> and <paramref name="curves" /> in one transaction and
    ///     removes the ones of other versions; with <paramref name="only" />, only the buckets of that type × interval, and the curve of that
    ///     interval only when the type is «Уровни» (decision 7 of add-own-idea-numbers).
    /// </summary>
    Task ReplaceAsync(
        int engineVersion,
        IReadOnlyCollection<IdeaBucket> buckets,
        IReadOnlyCollection<RiskRewardCurve> curves,
        (IdeaType Type, Interval Interval)? only,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<IdeaBucket>> GetAsync(int engineVersion, CancellationToken cancellationToken);

    Task<IReadOnlyList<RiskRewardCurve>> GetCurvesAsync(int engineVersion, CancellationToken cancellationToken);
}