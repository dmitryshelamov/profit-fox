using ProfitFox.Bench.MarketData.History;
using ProfitFox.Engine.Strategy.Execution;
using ProfitFox.Engine.Strategy.Graph;

namespace ProfitFox.Bench.Signals;

/// <summary>
///     The events of a signal on one pair, as both the bench run and the preview see them (spec signal-preview): the
///     executor gets only the candles with trades, each with the last funding payment paid by its close. Every event is
///     returned, in time order; a run keeps only those whose trade fits its period.
/// </summary>
public static class PairEvents
{
    public static IReadOnlyList<DateTimeOffset> Of(
        Signal signal,
        TimeSpan timeframe,
        string symbol,
        IEnumerable<HistoryCandle> candles,
        IEnumerable<HistoryPayment> funding)
    {
        var executor = new SignalExecutor(signal);
        FundingPayment[] payments = [.. funding.Select(payment => payment.ToFundingPayment()).OrderBy(payment => payment.Time)];
        int paid = 0;
        List<DateTimeOffset> events = [];
        foreach (HistoryCandle candle in candles.Where(candle => candle.HasTrades))
        {
            ClosedCandle closed = candle.ToClosedCandle(timeframe);
            while (paid < payments.Length && payments[paid].Time <= closed.CloseTime)
            {
                paid++;
            }

            if (executor.Step(symbol, closed, paid > 0 ? payments[paid - 1] : null) is { } found)
            {
                events.Add(found.Moment);
            }
        }

        return events;
    }
}
