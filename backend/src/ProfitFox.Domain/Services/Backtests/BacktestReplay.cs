using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Services.Backtests;

/// <summary>
///     Runs one combination of parameter values on stored candles, simulating the exchange. Inside a candle the price goes
///     Open → Low → High → Close. An order works from the candle after the event it was placed on; a cancel is confirmed at once.
///     A market order fills whole at the open, worse by <c>slippage</c> (a fraction); a limit order fills whole at its own price.
/// </summary>
public sealed class BacktestReplay(
    StrategySnapshot snapshot,
    IReadOnlyDictionary<string, NodeValue> parameterValues,
    TradingConditions conditions,
    decimal slippage,
    LimitFillMode mode,
    Interval interval)
{
    /// <summary>The strategy starts at <paramref name="start" /> and sees the candles one by one, oldest first.</summary>
    public void Run(DateTimeOffset start, IReadOnlyList<Candle> candles, IReplayObserver observer)
    {
        var run = StrategyRun.Start(snapshot.Definition, snapshot.Catalog(), parameterValues, conditions);
        new Pass(run, conditions, slippage, mode, interval, observer).Run(start, candles);
    }

    private sealed class Pass(StrategyRun run, TradingConditions conditions, decimal slippage, LimitFillMode mode, Interval interval, IReplayObserver observer)
    {
        private readonly List<SimulatedOrder> _orders = [];
        private readonly Queue<OrderCancelled> _cancels = new Queue<OrderCancelled>();
        private long _sequence;

        public void Run(DateTimeOffset start, IReadOnlyList<Candle> candles)
        {
            Handle(new RunStarted(start), 0);

            for (int index = 0; index < candles.Count; index++)
            {
                Candle candle = candles[index];

                foreach ((SimulatedOrder order, decimal price) in Executable(candle, index))
                {
                    if (_orders.Remove(order))
                    {
                        Fill(order, price, candle.OpenTime, index);
                    }
                }

                DateTimeOffset end = interval.CandleEnd(candle.OpenTime);
                Handle(new BarClosed(end, new Bar(candle.Open.Value, candle.High.Value, candle.Low.Value, candle.Close.Value, candle.Volume.Value)), index + 1);
                observer.BarClosed(end, candle);
            }
        }

        /// <summary>
        ///     Orders the price reaches on the candle, in the order it reaches them: those executable at the open by placement, then buys on
        ///     the way down from the highest price, then sells on the way up from the lowest; equal prices by placement.
        /// </summary>
        private IEnumerable<(SimulatedOrder Order, decimal Price)> Executable(Candle candle, int index)
        {
            decimal open = candle.Open.Value;
            var reached = new List<(int Phase, decimal Rank, long Sequence, SimulatedOrder Order, decimal Price)>();

            foreach (SimulatedOrder order in _orders.Where(order => order.ActiveFrom <= index))
            {
                decimal limit = order.Price ?? 0m;

                if (order.Type == OrderType.Market)
                {
                    reached.Add((0, 0m, order.Sequence, order, order.Side == OrderSide.Buy ? open * (1m + slippage) : open * (1m - slippage)));
                }
                else if (order.Side == OrderSide.Buy && Beyond(limit, open))
                {
                    reached.Add((0, 0m, order.Sequence, order, limit));
                }
                else if (order.Side == OrderSide.Buy && Beyond(limit, candle.Low.Value))
                {
                    reached.Add((1, -limit, order.Sequence, order, limit));
                }
                else if (order.Side == OrderSide.Sell && Beyond(open, limit))
                {
                    reached.Add((0, 0m, order.Sequence, order, limit));
                }
                else if (order.Side == OrderSide.Sell && Beyond(candle.High.Value, limit))
                {
                    reached.Add((2, limit, order.Sequence, order, limit));
                }
            }

            return reached.OrderBy(entry => entry.Phase).ThenBy(entry => entry.Rank).ThenBy(entry => entry.Sequence).Select(entry => (entry.Order, entry.Price));
        }

        /// <summary>Whether the price has come to <paramref name="higher" /> from below: touching it is enough only in the touch mode.</summary>
        private bool Beyond(decimal higher, decimal lower)
        {
            return mode == LimitFillMode.Touch ? higher >= lower : higher > lower;
        }

        /// <summary>The fee is charged as on Bybit: in the coin of a spot buy, otherwise in the quote asset from the value of the fill.</summary>
        private void Fill(SimulatedOrder order, decimal price, DateTimeOffset at, int index)
        {
            decimal quantity = order.Quantity ?? Down(order.Amount!.Value / price, conditions.Rules.QuantityStep);
            decimal rate = order.Type == OrderType.Limit ? conditions.Fees.LimitRate : conditions.Fees.MarketRate;
            FeeAsset asset = conditions.Fees.AssetOf(order.Side);
            decimal fee = asset == FeeAsset.Base ? quantity * rate : price * quantity * rate;
            var fill = new ReplayFill(at, order.Id, order.Side, order.Type, price, quantity, fee, asset);
            Handle(new OrderFilled(at, order.Id, quantity, price, IsComplete: true, fill.Fee, fill.FeeAsset), index + 1);
            observer.Filled(fill);
        }

        private static decimal Down(decimal value, decimal step)
        {
            return value - value % step;
        }

        /// <summary>Handles the event and every cancel confirmation its answer leads to; new orders work from <paramref name="activeFrom" />.</summary>
        private void Handle(StrategyEvent @event, int activeFrom)
        {
            Answer(run.Handle(@event), @event.At, activeFrom);

            while (_cancels.TryDequeue(out OrderCancelled? cancelled))
            {
                Answer(run.Handle(cancelled), cancelled.At, activeFrom);
            }
        }

        private void Answer(IReadOnlyList<OrderCommand> commands, DateTimeOffset at, int activeFrom)
        {
            foreach (OrderCommand command in commands)
            {
                if (command is PlaceOrder place)
                {
                    _orders.Add(new SimulatedOrder(place.OrderId, place.Side, place.Type, place.Price, place.Quantity, place.Amount, activeFrom, _sequence++));
                }
                else
                {
                    _orders.RemoveAll(order => order.Id == command.OrderId);
                    _cancels.Enqueue(new OrderCancelled(at, command.OrderId));
                }
            }
        }
    }

    private sealed record SimulatedOrder(
        string Id,
        OrderSide Side,
        OrderType Type,
        decimal? Price,
        decimal? Quantity,
        decimal? Amount,
        int ActiveFrom,
        long Sequence);
}