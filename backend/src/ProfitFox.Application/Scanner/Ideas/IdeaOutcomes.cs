using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Scanner.Ideas;

/// <summary>
///     The outcomes of the signals by the exit rules in force (decision 7): one pass over the signals in the order of their key and firing, which
///     holds the signals of one key at a time. The signals of a key count as one open idea at a time (<see cref="ExitOutcomes.Counted" />); the
///     cases fired in the last 90 and 30 days sum into their buckets, and every case gives its outcome to the calibration.
/// </summary>
public static class IdeaOutcomes
{
    private static readonly int[] Days = [90, 30];

    public static async Task<IdeaOutcomesResult> WalkAsync(
        IAsyncEnumerable<IdeaSignal> signals,
        IdeaExitRules rules,
        Func<MarketCategory, FeeRates> fees,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var sums = new Dictionary<(IdeaBucketKey, int), Sums>();
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

        IdeaBucket[] buckets =
        [
            .. sums.Where(entry => entry.Value.Cases > 0)
                .Select(entry => new IdeaBucket(entry.Key.Item1, entry.Key.Item2, entry.Value.Statistics(IdeaExitRule.HasWindow(entry.Key.Item1.Type))))
        ];

        return new IdeaOutcomesResult(buckets, outcomes);
    }

    private static void Walk(
        List<IdeaSignal> key,
        IdeaExitRules rules,
        Func<MarketCategory, FeeRates> fees,
        DateTimeOffset now,
        Dictionary<(IdeaBucketKey, int), Sums> sums,
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

        foreach ((SignalPath path, IdeaTrack outcome) in ExitOutcomes.Counted(key.Select(signal => signal.Path), rule))
        {
            decimal? result = CaseResult.R(outcome, rates);

            if (result is { } r)
            {
                outcomes.Add(new IdeaOutcome(bucket, path.FiredAt, outcome.StatusAt, outcome.Status, r));
            }

            foreach (int days in Days.Where(days => path.FiredAt >= now.AddDays(-days)))
            {
                sums[(bucket, days)] = sums.GetValueOrDefault((bucket, days)).Plus(outcome.Status, result);
            }
        }
    }

    /// <summary>The counts of the outcomes of a bucket, the sums of their results and the signals it counted.</summary>
    private readonly record struct Sums(int Targets, int Stops, int Expired, decimal TotalR, decimal TotalSquaredR, int Signals)
    {
        public int Cases => Targets + Stops + Expired;

        // A signal without an entry only counts as a signal.
        public Sums Plus(IdeaStatus status, decimal? result)
        {
            if (result is not { } r)
            {
                return this with { Signals = Signals + 1 };
            }

            Sums counted = status switch
            {
                IdeaStatus.Target => this with { Targets = Targets + 1 },
                IdeaStatus.Stop => this with { Stops = Stops + 1 },
                _ => this with { Expired = Expired + 1 }
            };

            return counted with { TotalR = TotalR + r, TotalSquaredR = TotalSquaredR + (r * r), Signals = Signals + 1 };
        }

        public BucketStatistics Statistics(bool ofSignals)
        {
            return BucketStatisticsCalculator.Compute(Targets, Stops, Expired, TotalR, TotalSquaredR, ofSignals ? Signals : null);
        }
    }
}