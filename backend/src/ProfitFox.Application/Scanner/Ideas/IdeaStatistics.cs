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
    IIdeaEngineVersionRepository versions,
    BacktestSettings backtests,
    IdeasEngineVersion version,
    TimeProvider time)
{
    private static readonly TimeSpan Read = TimeSpan.FromDays(180);

    // The roots of the categories of the last whole pass (decision 4 of honest-idea-intervals); none before the first one. The two lanes of the
    // statistics run side by side, and a reference is written whole.
    private volatile IReadOnlyDictionary<int, IReadOnlyDictionary<MarketCategory, BucketModel.Root>>? _roots;

    /// <summary>Writes the start of the version of the rules when the scanner takes the lease; the first start of a version stays.</summary>
    public async Task StartVersionAsync(CancellationToken cancellationToken)
    {
        await versions.StartAsync(version.Number, time.GetUtcNow(), cancellationToken);
    }

    /// <summary>The whole recompute; gives the types × intervals whose exit rule changed while it ran, which it counted by the rule before.</summary>
    public async Task<IReadOnlyList<(IdeaType Type, Interval Interval)>> RecomputeAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        IReadOnlyList<IdeaExitRule> used = await exitRules.GetAllAsync(cancellationToken);
        IdeaOutcomesResult result = await WalkAsync(now, null, used, null, cancellationToken);
        await buckets.ReplaceAsync(version.Number, result.Buckets, null, cancellationToken);
        _roots = result.Roots;
        await calibrations.ReplaceAsync(version.Number, CalibrationCalculator.Compute(result.Outcomes, now), cancellationToken);

        return [.. (await exitRules.GetAllAsync(cancellationToken)).Except(used).Select(rule => (rule.Type, rule.Interval))];
    }

    /// <summary>The recompute of the buckets of a type × interval, shrunk to the categories of the last whole pass; none before the first one.</summary>
    public async Task RecomputeAsync(IdeaType type, Interval interval, CancellationToken cancellationToken)
    {
        if (_roots is not { } roots)
        {
            return;
        }

        IdeaOutcomesResult result = await WalkAsync(time.GetUtcNow(), (type, interval), await exitRules.GetAllAsync(cancellationToken), roots, cancellationToken);
        await buckets.ReplaceAsync(version.Number, result.Buckets, (type, interval), cancellationToken);
    }

    private async Task<IdeaOutcomesResult> WalkAsync(
        DateTimeOffset now,
        (IdeaType, Interval)? only,
        IReadOnlyList<IdeaExitRule> applied,
        IReadOnlyDictionary<int, IReadOnlyDictionary<MarketCategory, BucketModel.Root>>? roots,
        CancellationToken cancellationToken)
    {
        var rules = new IdeaExitRules(applied);
        IReadOnlyDictionary<MarketCategory, FeeRates> fees = backtests.Policy().DefaultFees;

        return await IdeaOutcomes.WalkAsync(
            signals.StreamAsync(version.Number, now - Read, only, cancellationToken),
            rules,
            category => fees[category],
            now,
            cancellationToken,
            roots);
    }
}