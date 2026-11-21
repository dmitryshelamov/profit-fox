using ProfitFox.Domain.Execution;
using ProfitFox.Domain.Graph;
using ProfitFox.Domain.MarketData;

namespace ProfitFox.Domain.Signals;

/// <summary>
///     The events of a signal on one pair, as the preview sees them (spec signal-preview): the executor gets only the
///     candles with trades, each with the last funding payment paid by its close (<see cref="PairCandles" />). Every event
///     is returned, in time order. A run of the bench executes the strategy of the hypothesis on the same candles
///     (<see cref="Strategies.PairSteps" />), whose orders come at these events.
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
        List<DateTimeOffset> events = [];
        foreach ((ClosedCandle closed, FundingPayment? lastFunding) in PairCandles.Closed(timeframe, candles, funding))
        {
            if (executor.Step(symbol, closed, lastFunding) is { } found)
            {
                events.Add(found.Moment);
            }
        }

        return events;
    }
}
