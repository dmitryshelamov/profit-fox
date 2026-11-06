using ProfitFox.Application.Backtests;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Scanner.Ideas;

/// <summary>
///     The statistics of the ideas by the exit rules in force (decision 7): the signals of the version fired in the last 180 days, one pass by
///     <see cref="IdeaOutcomes" />. The whole recompute replaces the buckets and the calibration (decision 6 of honest-idea-probability); the one
///     of a type × interval only its buckets.
/// </summary>
public sealed class IdeaStatistics(
    IIdeaSignalRepository signals,
    IIdeaExitRuleRepository exitRules,
    IIdeaBucketRepository buckets,
    IIdeaCalibrationRepository calibrations,
    BacktestSettings backtests,
    IdeasEngineVersion version,
    TimeProvider time)
{
    private static readonly TimeSpan Read = TimeSpan.FromDays(180);

    /// <summary>The whole recompute; gives the types × intervals whose exit rule changed while it ran, which it counted by the rule before.</summary>
    public async Task<IReadOnlyList<(IdeaType Type, Interval Interval)>> RecomputeAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        IReadOnlyList<IdeaExitRule> used = await exitRules.GetAllAsync(cancellationToken);
        IdeaOutcomesResult result = await WalkAsync(now, null, used, cancellationToken);
        await buckets.ReplaceAsync(version.Number, result.Buckets, null, cancellationToken);
        await calibrations.ReplaceAsync(version.Number, CalibrationCalculator.Compute(result.Outcomes, now), cancellationToken);

        return [.. (await exitRules.GetAllAsync(cancellationToken)).Except(used).Select(rule => (rule.Type, rule.Interval))];
    }

    public async Task RecomputeAsync(IdeaType type, Interval interval, CancellationToken cancellationToken)
    {
        IdeaOutcomesResult result = await WalkAsync(time.GetUtcNow(), (type, interval), await exitRules.GetAllAsync(cancellationToken), cancellationToken);
        await buckets.ReplaceAsync(version.Number, result.Buckets, (type, interval), cancellationToken);
    }

    private async Task<IdeaOutcomesResult> WalkAsync(DateTimeOffset now, (IdeaType, Interval)? only, IReadOnlyList<IdeaExitRule> applied, CancellationToken cancellationToken)
    {
        var rules = new IdeaExitRules(applied);
        IReadOnlyDictionary<MarketCategory, FeeRates> fees = backtests.Policy().DefaultFees;

        return await IdeaOutcomes.WalkAsync(signals.StreamAsync(version.Number, now - Read, only, cancellationToken), rules, category => fees[category], now, cancellationToken);
    }
}