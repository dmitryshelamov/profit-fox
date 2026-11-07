using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     The calibration of the buckets on outcomes they had not seen (decision 5 of honest-idea-intervals): one walk over the cases in the order of
///     time, in which a case enters the windows of its bucket, its parent and its category when it closes and leaves them 90 days after it fired.
///     Each case fired in the last 90 days is checked by <see cref="BucketModel" /> on the windows as they were when it fired, the spreads between
///     the buckets and between the parents as they were at the start of its day UTC, and compared with its outcome.
/// </summary>
public static class CalibrationCalculator
{
    private static readonly int[] Days = [90, 30];
    private static readonly MarketCategory?[] Categories = [null, MarketCategory.Linear, MarketCategory.Spot];
    private static readonly CalibrationScope[] Scopes = [CalibrationScope.All, CalibrationScope.Fresh];
    private static readonly TimeSpan History = TimeSpan.FromDays(90);
    private static readonly decimal?[] ProbabilityBounds = [null, 0.2m, 0.3m, 0.4m, 0.5m, 0.6m, null];
    private static readonly decimal?[] ExpectancyBounds = [null, -0.2m, 0m, 0.2m, 0.4m, null];
    private static readonly IdeaBadge[] Badges = [IdeaBadge.Up, IdeaBadge.Flat, IdeaBadge.Down];

    /// <summary>
    ///     The reports of 90 and 30 days for all categories, linear and spot, of all cases and of those after the rules changed, from the cases
    ///     fired since 180 days before <paramref name="now" />; a case is checked by the same statistics in both scopes.
    /// </summary>
    /// <param name="outcomes"></param>
    /// <param name="now"></param>
    /// <param name="freshFrom">The time after which a case of a type × interval fired after its rules changed, for the reports of that scope.</param>
    /// <param name="holding">The holding of a type × interval by the rule the cases were counted by (decision 1 of cluster-idea-cases-by-holding).</param>
    public static IReadOnlyList<CalibrationReport> Compute(
        IReadOnlyList<IdeaOutcome> outcomes,
        DateTimeOffset now,
        Func<IdeaType, Interval, DateTimeOffset> freshFrom,
        Func<IdeaType, Interval, TimeSpan> holding)
    {
        List<Promise> promises = new Walk(outcomes, holding).Check(now.AddDays(-Days.Max()));
        bool[] fresh = [.. promises.Select(promise => promise.Case.FiredAt > freshFrom(promise.Case.Key.Type, promise.Case.Key.Interval))];

        return [.. Scopes.SelectMany(scope => Days.SelectMany(days => Categories.Select(category => Report(now, days, category, scope, promises, fresh, holding))))];
    }

    private static CalibrationReport Report(
        DateTimeOffset now,
        int days,
        MarketCategory? category,
        CalibrationScope scope,
        List<Promise> promises,
        bool[] fresh,
        Func<IdeaType, Interval, TimeSpan> holding)
    {
        Promise[] fired =
        [
            .. promises.Where((promise, index) => promise.Case.FiredAt >= now.AddDays(-days)
                                                  && (category is null || promise.Case.Key.Category == category)
                                                  && (scope == CalibrationScope.All || fresh[index]))
        ];
        Promise[] checkedCases = [.. fired.Where(promise => promise.Bucket is not null)];
        // Review of cluster-idea-cases-by-holding: every row of the report groups by one block, the longest holding among its cases, so its rows
        // compare.
        TimeSpan block = checkedCases.Select(promise => holding(promise.Case.Key.Type, promise.Case.Key.Interval)).DefaultIfEmpty().Max();

        return new CalibrationReport(
            now,
            days,
            category,
            checkedCases.Length,
            fired.Length - checkedCases.Length,
            Brier(checkedCases, PromisedProbability),
            Brier(checkedCases, promise => promise.BaseShare!.Value),
            Ranges(checkedCases, ProbabilityBounds, PromisedProbability, block),
            Ranges(checkedCases, ExpectancyBounds, PromisedExpectancy, block),
            [.. Badges.Select(badge => Row(null, null, badge, [.. checkedCases.Where(promise => promise.Bucket!.Badge == badge)], PromisedExpectancy, block)).OfType<CalibrationRow>()],
            Row(null, null, null, checkedCases, PromisedExpectancy, block))
        {
            Scope = scope,
            Owns =
            [
                .. new[] { false, true }
                    .Select(few => checkedCases.Where(promise => promise.Bucket!.FewOwnCases == few).ToArray())
                    .Where(cases => cases.Length > 0)
                    .Select(cases => new CalibrationOwnRow(cases[0].Bucket!.FewOwnCases, Row(null, null, null, cases, PromisedExpectancy, block)!, Brier(cases, PromisedProbability)!.Value))
            ],
            Comparison = Comparison([.. checkedCases.Where(promise => promise.Old is not null)], block)
        };
    }

    // The old model and the new one on the cases both check: the new one checks every case the old one does, unless its category has no root.
    private static CalibrationComparison Comparison(Promise[] compared, TimeSpan block)
    {
        CalibrationModel Model(Func<Promise, BucketStatistics> promise)
        {
            return new CalibrationModel(
                compared.Length,
                Brier(compared, item => promise(item).Probability!.Value),
                Row(null, null, IdeaBadge.Up, [.. compared.Where(item => promise(item).Badge == IdeaBadge.Up)], item => promise(item).Expectancy, block));
        }

        return new CalibrationComparison(Model(promise => promise.Old!), Model(promise => promise.Bucket!));
    }

    // Half-open ranges: a promise on a bound goes to the row that starts at it.
    private static CalibrationRow[] Ranges(Promise[] promises, decimal?[] bounds, Func<Promise, decimal> promised, TimeSpan block)
    {
        return
        [
            .. bounds.Zip(bounds.Skip(1))
                .Select(range => Row(
                    range.First,
                    range.Second,
                    null,
                    [.. promises.Where(promise => promised(promise) >= (range.First ?? decimal.MinValue) && promised(promise) < (range.Second ?? decimal.MaxValue))],
                    promised,
                    block))
                .OfType<CalibrationRow>()
        ];
    }

    // None without cases; the outcome of the cases of a row by their clustered sums, those fired in one block of the report being one group.
    private static CalibrationRow? Row(decimal? from, decimal? to, IdeaBadge? badge, Promise[] promises, Func<Promise, decimal> promised, TimeSpan block)
    {
        if (promises.Length == 0)
        {
            return null;
        }

        var outcome = new ClusteredSums(block);

        foreach (Promise promise in promises)
        {
            outcome.Add(promise.Case.FiredAt, promise.Case.Status, promise.Case.Result);
        }

        return new CalibrationRow(from, to, badge, promises.Average(promised), outcome.Statistics());
    }

    private static decimal? Brier(Promise[] promises, Func<Promise, decimal> promised)
    {
        return promises.Length == 0 ? null : promises.Average(promise => Square(promised(promise) - (promise.Case.Status == IdeaStatus.Target ? 1m : 0m)));
    }

    private static decimal PromisedProbability(Promise promise)
    {
        return promise.Bucket!.Probability!.Value;
    }

    private static decimal PromisedExpectancy(Promise promise)
    {
        return promise.Bucket!.Expectancy;
    }

    private static decimal Square(decimal value)
    {
        return value * value;
    }

    /// <summary>
    ///     A case and what it was promised: the model of its bucket, none without a model of its parent, the share of its category, and the old
    ///     model, the own window of its bucket by <see cref="BucketStatisticsCalculator" />, none under 30 own cases.
    /// </summary>
    private sealed record Promise(IdeaOutcome Case, BucketStatistics? Bucket, decimal? BaseShare, BucketStatistics? Old);

    /// <summary>
    ///     The walk over the cases: the windows of the buckets, the parents and the categories by number, which cases join by their close and leave
    ///     by their firing, and the spreads of the start of the day of the case checked.
    /// </summary>
    private sealed class Walk
    {
        private readonly IReadOnlyList<IdeaOutcome> _outcomes;
        private readonly int[] _bucketOf;
        private readonly int[] _parentOf;
        private readonly int[] _categoryOf;
        private readonly ClusteredSums[] _buckets;
        private readonly ClusteredSums[] _parents;
        private readonly ClusteredSums[] _categories;
        private readonly int[][] _bucketsOfParent;
        private readonly int[][] _parentsOfCategory;
        private readonly BucketModel.Spreads[] _parentSpreads;
        private readonly BucketModel.Spreads[] _categorySpreads;
        private readonly bool[] _inside;
        private readonly bool[] _gone;
        private readonly int[] _byFiring;
        private readonly int[] _byClose;
        private int _leaving;
        private int _closing;

        // The buckets and the parents group their cases by the blocks of their own holding, the categories by the longest holding among their
        // buckets (decisions 1 and 2 of cluster-idea-cases-by-holding).
        public Walk(IReadOnlyList<IdeaOutcome> outcomes, Func<IdeaType, Interval, TimeSpan> holding)
        {
            _outcomes = outcomes;
            _bucketOf = Numbers(outcomes.Select(outcome => outcome.Key), out IdeaBucketKey[] buckets);
            _parentOf = Numbers(buckets.Select(BucketModel.Parent.Of), out BucketModel.Parent[] parents);
            _categoryOf = Numbers(parents.Select(parent => parent.Category), out _);
            _buckets = [.. buckets.Select(bucket => new ClusteredSums(holding(bucket.Type, bucket.Interval)))];
            _parents = [.. parents.Select(parent => new ClusteredSums(holding(parent.Type, parent.Interval)))];
            _categories =
            [
                .. Enumerable.Range(0, _categoryOf.Distinct().Count())
                    .Select(category => new ClusteredSums(parents.Where((_, parent) => _categoryOf[parent] == category).Max(parent => holding(parent.Type, parent.Interval))))
            ];
            _bucketsOfParent = [.. Enumerable.Range(0, parents.Length).Select(parent => Enumerable.Range(0, buckets.Length).Where(bucket => _parentOf[bucket] == parent).ToArray())];
            _parentsOfCategory = [.. Enumerable.Range(0, _categories.Length).Select(category => Enumerable.Range(0, parents.Length).Where(parent => _categoryOf[parent] == category).ToArray())];
            _parentSpreads = new BucketModel.Spreads[parents.Length];
            _categorySpreads = new BucketModel.Spreads[_categories.Length];
            _inside = new bool[outcomes.Count];
            _gone = new bool[outcomes.Count];
            _byFiring = [.. Enumerable.Range(0, outcomes.Count).OrderBy(index => outcomes[index].FiredAt)];
            _byClose = [.. Enumerable.Range(0, outcomes.Count).OrderBy(index => outcomes[index].StatusAt)];
        }

        /// <summary>The promises of the cases fired since <paramref name="from" />.</summary>
        public List<Promise> Check(DateTimeOffset from)
        {
            var promises = new List<Promise>();
            DateTimeOffset? today = null;

            foreach (int index in _byFiring.Where(index => _outcomes[index].FiredAt >= from))
            {
                IdeaOutcome outcome = _outcomes[index];
                var day = new DateTimeOffset(outcome.FiredAt.UtcDateTime.Date, TimeSpan.Zero);

                if (day != today)
                {
                    MoveTo(day);
                    Spread();
                    today = day;
                }

                MoveTo(outcome.FiredAt);
                promises.Add(Promise(index));
            }

            return promises;
        }

        // The cases fired at most 90 days before the moment and closed before it are in the windows; strictly, as a case fired at that moment sees them.
        private void MoveTo(DateTimeOffset moment)
        {
            for (; _leaving < _byFiring.Length && _outcomes[_byFiring[_leaving]].FiredAt < moment - History; _leaving++)
            {
                if (_inside[_byFiring[_leaving]])
                {
                    Change(_byFiring[_leaving], -1);
                }

                _gone[_byFiring[_leaving]] = true;
            }

            for (; _closing < _byClose.Length && _outcomes[_byClose[_closing]].StatusAt < moment; _closing++)
            {
                if (!_gone[_byClose[_closing]])
                {
                    Change(_byClose[_closing], 1);
                    _inside[_byClose[_closing]] = true;
                }
            }
        }

        private void Change(int index, int sign)
        {
            IdeaOutcome outcome = _outcomes[index];
            int bucket = _bucketOf[index];
            int parent = _parentOf[bucket];

            foreach (ClusteredSums sums in new[] { _buckets[bucket], _parents[parent], _categories[_categoryOf[parent]] })
            {
                if (sign > 0)
                {
                    sums.Add(outcome.FiredAt, outcome.Status, outcome.Result);
                }
                else
                {
                    sums.Remove(outcome.FiredAt, outcome.Status, outcome.Result);
                }
            }
        }

        // The spreads between the parents of each category and between the buckets of each parent, by the windows of the moment.
        private void Spread()
        {
            for (int category = 0; category < _categories.Length; category++)
            {
                BucketModel.Level? level = BucketModel.Category(_categories[category]);
                _categorySpreads[category] = level is null ? default : BucketModel.Between(_parentsOfCategory[category].Select(parent => _parents[parent]), level);

                foreach (int parent in _parentsOfCategory[category])
                {
                    BucketModel.ParentModel? model = BucketModel.Model(_parents[parent], level is null ? null : new BucketModel.Root(level, _categorySpreads[category]));
                    _parentSpreads[parent] = model is null ? default : BucketModel.Between(_bucketsOfParent[parent].Select(bucket => _buckets[bucket]), model.Own);
                }
            }
        }

        private Promise Promise(int index)
        {
            int bucket = _bucketOf[index];
            int parent = _parentOf[bucket];
            int category = _categoryOf[parent];
            BucketModel.Level? level = BucketModel.Category(_categories[category]);
            BucketModel.ParentModel? model = level is null ? null : BucketModel.Model(_parents[parent], new BucketModel.Root(level, _categorySpreads[category]));

            ClusteredSums own = _buckets[bucket];
            BucketStatistics? old = own.Cases < BucketStatisticsCalculator.MinimumCases
                ? null
                : BucketStatisticsCalculator.Compute(own.Targets, own.Stops, own.Expired, own.TotalR, own.TotalSquaredR);

            return model is null
                ? new Promise(_outcomes[index], null, null, old)
                : new Promise(_outcomes[index], BucketModel.Statistics(own, model, _parentSpreads[parent]), level!.Share.Mean, old);
        }

        // The number of each item among the distinct ones, in the order they first come.
        private static int[] Numbers<TKey>(IEnumerable<TKey> items, out TKey[] distinct)
            where TKey : notnull
        {
            var numbers = new Dictionary<TKey, int>();
            int[] numbered = [.. items.Select(item => numbers.TryGetValue(item, out int number) ? number : numbers[item] = numbers.Count)];
            distinct = [.. numbers.OrderBy(entry => entry.Value).Select(entry => entry.Key)];

            return numbered;
        }
    }
}