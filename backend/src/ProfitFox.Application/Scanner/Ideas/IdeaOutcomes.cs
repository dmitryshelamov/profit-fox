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
    /// <param name="burstOf">
    ///     The count of the distinct pairs of the side and interval of a signal in its minute, which gives the burst of its case (decision 2 of
    ///     add-burst-and-auto-limits); none counts none.
    /// </param>
    /// <param name="keep">The most cases whose outcomes the pass keeps for the calibration; a pass that counts more gives none of them.</param>
    public static async Task<IdeaOutcomesResult> WalkAsync(
        IAsyncEnumerable<IdeaSignal> signals,
        IdeaExitRules rules,
        Func<MarketCategory, FeeRates> fees,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<int, IReadOnlyDictionary<MarketCategory, BucketModel.Root>>? roots = null,
        Func<IdeaSignal, int?>? burstOf = null,
        int keep = int.MaxValue)
    {
        var sums = new Sums();
        var outcomes = new Kept(keep);
        var key = new List<IdeaSignal>();

        await foreach (IdeaSignal signal in signals.WithCancellation(cancellationToken))
        {
            if (key.Count > 0 && key[0].Key != signal.Key)
            {
                Walk(key, rules, fees, now, sums, outcomes, burstOf);
                key.Clear();
            }

            key.Add(signal);
        }

        Walk(key, rules, fees, now, sums, outcomes, burstOf);
        sums.SumCategories(now);

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

        return new IdeaOutcomesResult(buckets, outcomes.Outcomes, used);
    }

    private static void Walk(
        List<IdeaSignal> key,
        IdeaExitRules rules,
        Func<MarketCategory, FeeRates> fees,
        DateTimeOffset now,
        Sums sums,
        Kept outcomes,
        Func<IdeaSignal, int?>? burstOf)
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
                sums.Add(bucket.Category, holding, path.FiredAt, outcome.Status, r);

                if (outcomes.Count())
                {
                    IdeaSignal signal = signals[path];
                    outcomes.Outcomes!.Add(new IdeaOutcome(bucket, path.FiredAt, outcome.StatusAt, outcome.Status, r, signal.FeaturesWith(burstOf?.Invoke(signal))));
                }
            }

            foreach (int days in Days.Where(days => path.FiredAt >= now.AddDays(-days)))
            {
                sums.Add(bucket, days, holding, path.FiredAt, outcome.Status, result);
            }
        }
    }

    // The outcomes of the cases for the calibration, which walks all of them or none: none once the pass counted more cases than it may keep.
    private sealed class Kept(int limit)
    {
        private int _counted;

        public List<IdeaOutcome>? Outcomes { get; private set; } = [];

        /// <summary>Counts a case; tells whether the pass still keeps the outcomes.</summary>
        public bool Count()
        {
            if (++_counted > limit)
            {
                Outcomes = null;
            }

            return Outcomes is not null;
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

        // Decision 2 of cluster-idea-cases-by-holding: a category mixes the types × intervals, so its groups are the blocks of the longest holding
        // among its cases, known only once the pass is over. Until then its cases are the sums of each firing, which no block splits.
        private readonly Dictionary<MarketCategory, CategoryCases> _cases = [];

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

        /// <summary>A case of a category, of any age, with the holding of its rule.</summary>
        public void Add(MarketCategory category, TimeSpan holding, DateTimeOffset firedAt, IdeaStatus status, decimal result)
        {
            if (!_cases.TryGetValue(category, out CategoryCases? cases))
            {
                cases = new CategoryCases();
                _cases[category] = cases;
            }

            cases.Longest = holding > cases.Longest ? holding : cases.Longest;
            Firing before = cases.Firings.GetValueOrDefault(firedAt);
            cases.Firings[firedAt] = new Firing(
                before.Targets + (status == IdeaStatus.Target ? 1 : 0),
                before.Stops + (status == IdeaStatus.Stop ? 1 : 0),
                before.Expired + (status is IdeaStatus.Target or IdeaStatus.Stop ? 0 : 1),
                before.Results + result,
                before.SquaredResults + (result * result));
        }

        /// <summary>The sums of the categories by the blocks of the longest holding of their cases, once the pass is over.</summary>
        public void SumCategories(DateTimeOffset now)
        {
            foreach ((MarketCategory category, CategoryCases cases) in _cases)
            {
                foreach (int days in Days)
                {
                    var sums = new ClusteredSums(cases.Longest);

                    foreach ((DateTimeOffset firedAt, Firing firing) in cases.Firings.Where(firing => firing.Key >= now.AddDays(-days)))
                    {
                        sums.Add(firedAt, firing.Targets, firing.Stops, firing.Expired, firing.Results, firing.SquaredResults);
                    }

                    Categories[days][category] = sums;
                }
            }
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

        private sealed class CategoryCases
        {
            public TimeSpan Longest { get; set; }

            public Dictionary<DateTimeOffset, Firing> Firings { get; } = [];
        }

        // The cases of a category fired at one moment: the counts of their outcomes, the total of their results and that of the squares of them.
        private readonly record struct Firing(int Targets, int Stops, int Expired, decimal Results, decimal SquaredResults);
    }
}