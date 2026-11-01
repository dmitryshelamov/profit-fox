using ProfitFox.Domain.Entities.MarketData;

namespace ProfitFox.Domain.Services.Backtests;

/// <summary>Watches a replay: each call comes after the strategy has handled the event and its answer.</summary>
public interface IReplayObserver
{
    void Filled(ReplayFill fill);

    /// <param name="at">The end of the candle.</param>
    /// <param name="candle"></param>
    void BarClosed(DateTimeOffset at, Candle candle);
}