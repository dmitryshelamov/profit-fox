using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;

namespace ProfitFox.Domain.ValueObjects.IdeaTrades;

/// <summary>
///     The totals of the closed trades of a journal: the trades closed at the target, at the stop and manually, and apart from them the cancelled
///     and the failed, which have no result. The money, the fees, the mean result in R with its 95 % interval (none for one trade) and the mean
///     time to the outcome are over the first three; so are the two comparisons.
/// </summary>
public sealed record IdeaJournalTotals(
    int Targets,
    int Stops,
    int Manual,
    int Cancelled,
    int Failed,
    decimal Result,
    decimal Fees,
    decimal? MeanR,
    decimal? MeanRLow,
    decimal? MeanRHigh,
    TimeSpan? MeanTime,
    PromiseComparison Promise,
    IdeaComparison Idea)
{
    // The 97.5th percentile of the standard normal distribution: a 95 % interval.
    private const double Z = 1.96;

    public static IdeaJournalTotals Compute(IReadOnlyCollection<IdeaTrade> closed)
    {
        IReadOnlyList<IdeaTrade> settled = [.. closed.Where(trade => trade.Status is IdeaTradeStatus.ClosedAtTarget or IdeaTradeStatus.ClosedAtStop or IdeaTradeStatus.ClosedManually)];
        IReadOnlyList<decimal> results = [.. settled.Select(trade => trade.ResultR!.Value)];
        // A trade an older Worker opened during the rollout has no open time, so it has no time to its outcome either.
        IReadOnlyList<TimeSpan> times = [.. settled.Select(trade => trade.TimeToOutcome).OfType<TimeSpan>()];
        (decimal? mean, decimal? low, decimal? high) = Interval(results);
        IReadOnlyList<IdeaTrade> promised = [.. settled.Where(trade => trade.Promise?.Expectancy is not null)];
        IReadOnlyList<IdeaTrade> ideas = [.. settled.Where(trade => trade.IdeaOutcome?.Result is not null)];

        return new IdeaJournalTotals(
            Count(closed, IdeaTradeStatus.ClosedAtTarget),
            Count(closed, IdeaTradeStatus.ClosedAtStop),
            Count(closed, IdeaTradeStatus.ClosedManually),
            Count(closed, IdeaTradeStatus.Cancelled),
            Count(closed, IdeaTradeStatus.Failed),
            settled.Sum(trade => trade.Result!.Value),
            settled.Sum(trade => trade.Fees),
            mean,
            low,
            high,
            times.Count == 0 ? null : TimeSpan.FromTicks((long)times.Average(time => time.Ticks)),
            new PromiseComparison(promised.Count, Mean(promised.Select(trade => trade.Promise!.Expectancy!.Value)), Mean(promised.Select(trade => trade.ResultR!.Value))),
            new IdeaComparison(ideas.Count, Mean(ideas.Select(trade => trade.IdeaOutcome!.Result!.Value)), Mean(ideas.Select(trade => trade.ResultR!.Value))));
    }

    private static int Count(IEnumerable<IdeaTrade> trades, IdeaTradeStatus status)
    {
        return trades.Count(trade => trade.Status == status);
    }

    private static decimal? Mean(IEnumerable<decimal> values)
    {
        IReadOnlyList<decimal> list = [.. values];

        return list.Count == 0 ? null : list.Average();
    }

    private static (decimal? Mean, decimal? Low, decimal? High) Interval(IReadOnlyList<decimal> results)
    {
        if (results.Count == 0)
        {
            return (null, null, null);
        }

        decimal mean = results.Average();

        if (results.Count == 1)
        {
            return (mean, null, null);
        }

        decimal variance = results.Sum(result => (result - mean) * (result - mean)) / (results.Count - 1);
        var margin = (decimal)(Z * Math.Sqrt((double)(variance / results.Count)));

        return (mean, mean - margin, mean + margin);
    }
}