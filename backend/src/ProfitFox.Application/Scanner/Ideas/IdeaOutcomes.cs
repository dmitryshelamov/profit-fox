using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Scanner.Ideas;

/// <summary>
///     The outcomes of the signals by the exit rules in force (decision 7): one pass over the signals in the order of their key and firing, which
///     holds the signals of one key at a time. The signals of a key count as one open idea at a time (<see cref="ExitOutcomes.Counted" />); the
///     cases fired in the last 90 and 30 days go into the clustered sums of their bucket, of its parent and of its category by the block of their
///     firing (decision 4 of honest-idea-intervals, decisions 1 and 2 of cluster-idea-cases-by-holding), and every case gives its outcome and the
///     features of its signal to the calibration. After the pass the buckets get the numbers of <see cref="BucketModel" />; the parents and the
///     categories are not written.
/// </summary>
public static class IdeaOutcomes
{
    private static readonly int[] Days = [90, 30];

    /// <param name="signals"></param>

    /// <param name="rules"></param>
    /// <param name="fees"></param>
    /// <param name="now"></param>
    /// <param name="cancellationToken"></param>    /// <param name="roots">The roots of the categories by the days of the window, of the last whole pass; none takes those of this pass.</param>
    public static async Task<IdeaOutcomesResult> WalkAsync(
        IAsyncEnumerable<IdeaSignal> signals,
        IdeaExitRules rules,
        Func<MarketCategory, FeeRates> fees,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<int, IReadOnlyDictionary<MarketCategory, BucketModel.Root>>? roots = null)
    {
        var sums = new Sums();
        var outcomes = new List<IdeaOutcome>();
        var key = new List<IdeaSignal>();

        await foreach (IdeaSignal signal in signals.WithCancellation(cancellationToken))
        {
            if (key.Count > 0 && key[0].Key != signal.Key)
            {
                Walk(key, rules, fees, now, sums, outcomes);
                key.Clear();
            }

            key.Add(signal);
        }

        Walk(key, rules, fees, now, sums, outcomes);
        SumCategories(outcomes, rules, now, sums);

        Dictionary<int, IReadOnlyDictionary<MarketCategory, BucketModel.Root>> used = Days.ToDictionary(
            days => days,
            days => roots?[days] ?? BucketModel.Roots(sums.Parents[days], sums.Categories[days]));
        IdeaBucket[] buckets =
        [
            .. Days.SelectMany(days => BucketModel.Build(sums.Buckets[days], sums.Parents[days], used[days])
                .Select(bucket => new IdeaBucket(
                    bucket.Key,
                    days,
                    bucket.Value with { Signals = IdeaExitRule.HasWindow(bucket.Key.Type) ? sums.Signals[(bucket.Key, days)] : null })))
        ];

        return new IdeaOutcomesResult(buckets, outcomes, used);
    }

    private static void Walk(
        List<IdeaSignal> key,
        IdeaExitRules rules,
        Func<MarketCategory, FeeRates> fees,
        DateTimeOffset now,
        Sums sums,
        List<IdeaOutcome> outcomes)
    {
        if (key.Count == 0)
        {
            return;
        }

        IdeaSignal first = key[0];
        IdeaBucketKey bucket = first.Bucket;
        FeeRates rates = fees(bucket.Category);
        IdeaExitRule rule = rules.Of(bucket.Type, bucket.Interval);
        TimeSpan holding = rule.Holding;
        // The paths counted are those of the signals, which give the features of their cases.
        var signals = key.ToDictionary<IdeaSignal, SignalPath>(signal => signal.Path, ReferenceEqualityComparer.Instance);

        foreach ((SignalPath path, IdeaTrack outcome) in ExitOutcomes.Counted(key.Select(signal => signal.Path), rule))
        {
            decimal? result = CaseResult.R(outcome, rates);

            if (result is { } r)
            {
                IdeaSignal signal = signals[path];
                outcomes.Add(new IdeaOutcome(bucket, path.FiredAt, outcome.StatusAt, outcome.Status, r, IdeaFeatures.Of(signal.Move, signal.VolumeRatio)));
            }

            foreach (int days in Days.Where(days => path.FiredAt >= now.AddDays(-days)))
            {
                sums.Add(bucket, days, holding, path.FiredAt, outcome.Status, result);
            }
        }
    }

    // Decision 2 of cluster-idea-cases-by-holding: a category mixes the types × intervals, so its groups are the blocks of the longest holding
    // among its cases, known only once the pass is over.
    private static void SumCategories(List<IdeaOutcome> outcomes, IdeaExitRules rules, DateTimeOffset now, Sums sums)
    {
        foreach (IGrouping<MarketCategory, IdeaOutcome> category in outcomes.GroupBy(outcome => outcome.Key.Category))
        {
            TimeSpan longest = category.Max(outcome => rules.Of(outcome.Key.Type, outcome.Key.Interval).Holding);

            foreach (int days in Days)
            {
                var categorySums = new ClusteredSums(longest);

                foreach (IdeaOutcome outcome in category.Where(outcome => outcome.FiredAt >= now.AddDays(-days)))
                {
                    categorySums.Add(outcome.FiredAt, outcome.Status, outcome.Result);
                }

                sums.Categories[days][category.Key] = categorySums;
            }
        }
    }

    /// <summary>
    ///     The clustered sums of the buckets and their parents by the blocks of the holding of their rule, of their categories, by the days of the
    ///     window, and the signals the buckets counted.
    /// </summary>
    private sealed class Sums
    {
        public Dictionary<int, Dictionary<IdeaBucketKey, ClusteredSums>> Buckets { get; } = Days.ToDictionary(days => days, _ => new Dictionary<IdeaBucketKey, ClusteredSums>());

        public Dictionary<int, Dictionary<BucketModel.Parent, ClusteredSums>> Parents { get; } =
            Days.ToDictionary(days => days, _ => new Dictionary<BucketModel.Parent, ClusteredSums>());

        public Dictionary<int, Dictionary<MarketCategory, ClusteredSums>> Categories { get; } = Days.ToDictionary(days => days, _ => new Dictionary<MarketCategory, ClusteredSums>());

        public Dictionary<(IdeaBucketKey, int), int> Signals { get; } = [];

        // A signal without an entry only counts as a signal.
        public void Add(IdeaBucketKey bucket, int days, TimeSpan holding, DateTimeOffset firedAt, IdeaStatus status, decimal? result)
        {
            Signals[(bucket, days)] = Signals.GetValueOrDefault((bucket, days)) + 1;

            if (result is not { } r)
            {
                return;
            }

            Of(Buckets[days], bucket, holding).Add(firedAt, status, r);
            Of(Parents[days], BucketModel.Parent.Of(bucket), holding).Add(firedAt, status, r);
        }

        private static ClusteredSums Of<TKey>(Dictionary<TKey, ClusteredSums> sums, TKey key, TimeSpan holding)
            where TKey : notnull
        {
            if (!sums.TryGetValue(key, out ClusteredSums? found))
            {
                found = new ClusteredSums(holding);
                sums[key] = found;
            }

            return found;
        }
    }
}