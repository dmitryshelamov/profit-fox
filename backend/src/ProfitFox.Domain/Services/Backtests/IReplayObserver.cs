using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Services.Strategies.Execution;

namespace ProfitFox.Domain.Services.Backtests;

/// <summary>Watches a replay: each call comes after the strategy has handled the event and its answer, with the state it left.</summary>
public interface IReplayObserver
{
    void Filled(ReplayFill fill, RunState state);

    /// <param name="at">The end of the candle.</param>
    /// <param name="candle"></param>
    /// <param name="state"></param>
    void BarClosed(DateTimeOffset at, Candle candle, RunState state);
}