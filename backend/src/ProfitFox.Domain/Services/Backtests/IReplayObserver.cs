using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Services.Backtests;

/// <summary>Watches a replay: each call comes after the strategy has handled the event and its answer, with the state it left.</summary>
public interface IReplayObserver
{
    /// <param name="order"></param>
    /// <param name="at">The time of the event the strategy answered with the order.</param>
    /// <param name="position">The number of the position the order belongs to.</param>
    void Placed(PlaceOrder order, DateTimeOffset at, int position);

    void Cancelled(string orderId, DateTimeOffset at);

    void Filled(ReplayFill fill, RunState state);

    /// <param name="at">The end of the candle.</param>
    /// <param name="candle"></param>
    /// <param name="state"></param>
    void BarClosed(DateTimeOffset at, Candle candle, RunState state);
}