using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     The calibration of the buckets on outcomes they had not seen (decision 6): each case fired in the last 90 days gets the statistics of its
///     bucket from the cases of the bucket closed before it fired and fired at most 90 days before it, by <see cref="BucketStatisticsCalculator" />,
///     and the share of targets of its category from the same window over all the buckets of the category; both are compared with its outcome.
/// </summary>
public static class CalibrationCalculator
{
    private static readonly int[] Days = [90, 30];
    private static readonly MarketCategory?[] Categories = [null, MarketCategory.Linear, MarketCategory.Spot];
    private static readonly TimeSpan History = TimeSpan.FromDays(90);
    private static readonly decimal?[] ProbabilityBounds = [null, 0.2m, 0.3m, 0.4m, 0.5m, 0.6m, null];
    private static readonly decimal?[] ExpectancyBounds = [null, -0.2m, 0m, 0.2m, 0.4m, null];
    private static readonly IdeaBadge[] Badges = [IdeaBadge.Up, IdeaBadge.Flat, IdeaBadge.Down];

    /// <summary>The reports of 90 and 30 days for all categories, linear and spot, from the cases fired since 180 days before <paramref name="now" />.</summary>
    public static IReadOnlyList<CalibrationReport> Compute(IReadOnlyList<IdeaOutcome> outcomes, DateTimeOffset now)
    {
        DateTimeOffset from = now.AddDays(-Days.Max());
        Sums?[] buckets = Windows(outcomes, outcome => outcome.Key, from);
        Sums?[] categories = Windows(outcomes, outcome => outcome.Key.Category, from);
        var promises = new List<Promise>();

        for (int index = 0; index < outcomes.Count; index++)
        {
            if (buckets[index] is not { } bucket)
            {
                continue;
            }

            // The window of the category holds the one of the bucket, so a checked case has at least 30 cases there too.
            Sums category = categories[index]!.Value;
            promises.Add(bucket.Cases < BucketStatisticsCalculator.MinimumCases
                ? new Promise(outcomes[index], null, null)
                : new Promise(outcomes[index], bucket.Statistics(), (decimal)category.Targets / category.Cases));
        }

        return [.. Days.SelectMany(days => Categories.Select(category => Report(now, days, category, promises)))];
    }

    private static CalibrationReport Report(DateTimeOffset now, int days, MarketCategory? category, List<Promise> promises)
    {
        Promise[] fired = [.. promises.Where(promise => promise.Case.FiredAt >= now.AddDays(-days) && (category is null || promise.Case.Key.Category == category))];
        Promise[] checkedCases = [.. fired.Where(promise => promise.Bucket is not null)];

        return new CalibrationReport(
            now,
            days,
            category,
            checkedCases.Length,
            fired.Length - checkedCases.Length,
            Brier(checkedCases, PromisedProbability),
            Brier(checkedCases, promise => promise.BaseShare!.Value),
            Ranges(checkedCases, ProbabilityBounds, PromisedProbability),
            Ranges(checkedCases, ExpectancyBounds, PromisedExpectancy),
            [.. Badges.Select(badge => Row(null, null, badge, [.. checkedCases.Where(promise => promise.Bucket!.Badge == badge)], PromisedExpectancy)).OfType<CalibrationRow>()],
            Row(null, null, null, checkedCases, PromisedExpectancy));
    }

    // Half-open ranges: a promise on a bound goes to the row that starts at it.
    private static CalibrationRow[] Ranges(Promise[] promises, decimal?[] bounds, Func<Promise, decimal> promised)
    {
        return
        [
            .. bounds.Zip(bounds.Skip(1))
                .Select(range => Row(
                    range.First,
                    range.Second,
                    null,
                    [.. promises.Where(promise => promised(promise) >= (range.First ?? decimal.MinValue) && promised(promise) < (range.Second ?? decimal.MaxValue))],
                    promised))
                .OfType<CalibrationRow>()
        ];
    }

    // None without cases.
    private static CalibrationRow? Row(decimal? from, decimal? to, IdeaBadge? badge, Promise[] promises, Func<Promise, decimal> promised)
    {
        if (promises.Length == 0)
        {
            return null;
        }

        Sums outcome = promises.Aggregate(new Sums(), (sums, promise) => sums.Plus(promise.Case));

        return new CalibrationRow(from, to, badge, promises.Average(promised), outcome.Statistics());
    }

    private static decimal? Brier(Promise[] promises, Func<Promise, decimal> promised)
    {
        return promises.Length == 0 ? null : promises.Average(promise => Square(promised(promise) - (promise.Case.Status == IdeaStatus.Target ? 1m : 0m)));
    }

    /// <summary>
    ///     The sums of the window of each case fired since <paramref name="from" />, none for the others: the cases of its group closed before it
    ///     fired and fired at most 90 days before it. Each group is walked once in the order of firing, the window taking cases in by their close
    ///     and letting them go by their firing.
    /// </summary>
    private static Sums?[] Windows<TKey>(IReadOnlyList<IdeaOutcome> outcomes, Func<IdeaOutcome, TKey> group, DateTimeOffset from)
    {
        var windows = new Sums?[outcomes.Count];
        var inside = new bool[outcomes.Count];
        var gone = new bool[outcomes.Count];

        foreach (IGrouping<TKey, int> members in Enumerable.Range(0, outcomes.Count).GroupBy(index => group(outcomes[index])))
        {
            int[] byFiring = [.. members.OrderBy(index => outcomes[index].FiredAt)];
            int[] byClose = [.. members.OrderBy(index => outcomes[index].StatusAt)];
            var window = new Sums();
            int leaving = 0;
            int closing = 0;

            foreach (int index in byFiring.Where(index => outcomes[index].FiredAt >= from))
            {
                DateTimeOffset firedAt = outcomes[index].FiredAt;

                for (; leaving < byFiring.Length && outcomes[byFiring[leaving]].FiredAt < firedAt - History; leaving++)
                {
                    if (inside[byFiring[leaving]])
                    {
                        window = window.Minus(outcomes[byFiring[leaving]]);
                    }

                    gone[byFiring[leaving]] = true;
                }

                for (; closing < byClose.Length && outcomes[byClose[closing]].StatusAt < firedAt; closing++)
                {
                    if (!gone[byClose[closing]])
                    {
                        window = window.Plus(outcomes[byClose[closing]]);
                        inside[byClose[closing]] = true;
                    }
                }

                windows[index] = window;
            }
        }

        return windows;
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

    private sealed record Promise(IdeaOutcome Case, BucketStatistics? Bucket, decimal? BaseShare);

    /// <summary>The counts of the outcomes of a window of cases and the sums of their results, which a case can add to and take from.</summary>
    private readonly record struct Sums(int Targets, int Stops, int Expired, decimal TotalR, decimal TotalSquaredR)
    {
        public int Cases => Targets + Stops + Expired;

        public Sums Plus(IdeaOutcome outcome)
        {
            return Add(outcome, 1);
        }

        public Sums Minus(IdeaOutcome outcome)
        {
            return Add(outcome, -1);
        }

        public BucketStatistics Statistics()
        {
            return BucketStatisticsCalculator.Compute(Targets, Stops, Expired, TotalR, TotalSquaredR);
        }

        private Sums Add(IdeaOutcome outcome, int sign)
        {
            Sums counted = outcome.Status switch
            {
                IdeaStatus.Target => this with { Targets = Targets + sign },
                IdeaStatus.Stop => this with { Stops = Stops + sign },
                _ => this with { Expired = Expired + sign }
            };

            return counted with { TotalR = TotalR + (sign * outcome.Result), TotalSquaredR = TotalSquaredR + (sign * Square(outcome.Result)) };
        }
    }
}