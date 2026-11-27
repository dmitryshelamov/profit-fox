using ProfitFox.Domain.Services.Execution;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Services.Signals;

/// <summary>
///     The events of a signal on one pair, as the preview sees them (spec signal-preview): the executor gets only the
///     candles with trades, each with the last funding payment paid by its close (<see cref="PairCandles" />). Every event
///     is returned, in time order. A backtest executes a strategy with this signal on the same candles
///     (<see cref="Strategies.PairSteps" />), whose orders come at these events.
/// </summary>
public static class PairEvents
{
    public static IReadOnlyList<DateTimeOffset> Of(
        Signal signal,
        TimeSpan timeframe,
        string symbol,
        IEnumerable<HistoryCandle> candles,
        IEnumerable<HistoryPayment> funding,
        IReadOnlyDictionary<string, IReadOnlyList<HistoryCandle>>? named = null)
    {
        var executor = new SignalExecutor(signal);
        List<DateTimeOffset> events = [];
        foreach ((ClosedCandle closed, CandleFacts facts, IReadOnlyDictionary<string, ClosedCandle>? pairs) in PairCandles.WithPairs(timeframe, candles, funding, named))
        {
            if (executor.Step(symbol, closed, facts, pairs) is { } found)
            {
                events.Add(found.Moment);
            }
        }

        return events;
    }
}
