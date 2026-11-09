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
///     of a type × interval only its buckets. The calibration walks the outcome of every case, which the pass keeps in memory, so over more
///     cases than <see cref="ScannerSettings.CalibrationCases" /> the whole recompute leaves it as it was.
/// </summary>
public sealed class IdeaStatistics(
    IIdeaSignalRepository signals,
    IIdeaExitRuleRepository exitRules,
    IIdeaBucketRepository buckets,
    IIdeaCalibrationRepository calibrations,
    IIdeaEngineVersionRepository versions,
    BacktestSettings backtests,
    ScannerSettings settings,
    IdeasEngineVersion version,
    TimeProvider time)
{
    private static readonly TimeSpan Read = TimeSpan.FromDays(180);

    // The roots of the categories of the last whole pass (decision 4 of honest-idea-intervals); none before the first one. The two lanes of the
    // statistics run side by side, and a reference is written whole.
    private volatile IdeaRoots? _roots;

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
        // Decision 2 of add-burst-and-auto-limits: the cases take the burst of their minute, counted from the signals the pass reads.
        IReadOnlyDictionary<(DateTimeOffset FiredAt, IdeaSide Side, Interval Interval), int> pairs = await signals.CountPairsByMinuteAsync(version.Number, now - Read, cancellationToken);
        IdeaOutcomesResult result = await WalkAsync(
            now,
            null,
            used,
            null,
            cancellationToken,
            settings.CalibrationCases,
            signal => pairs.TryGetValue((signal.Path.FiredAt, signal.Path.Side, signal.Path.Interval), out int count) ? count : null);
        await buckets.ReplaceAsync(version.Number, result.Buckets, result.Curves, null, cancellationToken);
        _roots = result.Roots;

        if (result.Outcomes is { } outcomes)
        {
            // Requirement "Проверка после изменения правил": a rule never applied is in force from the start of the version; without a start
            // written, no case is fresh.
            DateTimeOffset started = await versions.GetStartAsync(version.Number, cancellationToken) ?? now;
            var rules = new IdeaExitRules(used);
            await calibrations.ReplaceAsync(
                version.Number,
                CalibrationCalculator.Compute(
                    outcomes,
                    now,
                    (type, interval) => rules.Of(type, interval).AppliedAt is { } applied && applied > started ? applied : started,
                    (type, interval) => rules.Of(type, interval).Holding),
                cancellationToken);
        }

        return [.. (await exitRules.GetAllAsync(cancellationToken)).Except(used).Select(rule => (rule.Type, rule.Interval))];
    }

    /// <summary>The recompute of the buckets of a type × interval, shrunk to the categories of the last whole pass; none before the first one.</summary>
    public async Task RecomputeAsync(IdeaType type, Interval interval, CancellationToken cancellationToken)
    {
        if (_roots is not { } roots)
        {
            return;
        }

        // The buckets need no outcome kept.
        IdeaOutcomesResult result = await WalkAsync(time.GetUtcNow(), (type, interval), await exitRules.GetAllAsync(cancellationToken), roots, cancellationToken, 0);
        await buckets.ReplaceAsync(version.Number, result.Buckets, result.Curves, (type, interval), cancellationToken);
    }

    private async Task<IdeaOutcomesResult> WalkAsync(
        DateTimeOffset now,
        (IdeaType, Interval)? only,
        IReadOnlyList<IdeaExitRule> applied,
        IdeaRoots? roots,
        CancellationToken cancellationToken,
        int keep,
        Func<IdeaSignal, int?>? burstOf = null)
    {
        var rules = new IdeaExitRules(applied);
        IReadOnlyDictionary<MarketCategory, FeeRates> fees = backtests.Policy().DefaultFees;

        return await IdeaOutcomes.WalkAsync(
            signals.StreamAsync(version.Number, now - Read, only, cancellationToken),
            rules,
            category => fees[category],
            now,
            cancellationToken,
            roots,
            burstOf,
            keep);
    }
}