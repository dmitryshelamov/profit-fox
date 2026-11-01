using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.ValueObjects.Backtests;

namespace ProfitFox.Domain.Services.Backtests;

/// <summary>The metrics of a combination, as <see cref="MetricsObserver" /> collects them, with its fills, trades and equity curve.</summary>
public sealed class DetailsObserver(decimal balance) : IReplayObserver
{
    public const int MaxEquityPoints = 5000;

    private readonly MetricsObserver _metrics = new MetricsObserver(balance);
    private readonly List<ReplayFill> _fills = [];
    private readonly List<BacktestTrade> _trades = [];
    private readonly List<(DateTimeOffset At, decimal Equity)> _closes = [];
    private RunState? _last;
    private Cycle? _cycle;

    public void Filled(ReplayFill fill, RunState state)
    {
        _metrics.Filled(fill, state);
        _fills.Add(fill);
        RunState before = _last ?? state with { Cash = balance, Quantity = 0m, Dust = 0m, DustCollateral = 0m };
        _cycle ??= new Cycle(fill.At, before);
        _cycle.Add(fill, state.Direction);

        if (state.Quantity == 0m)
        {
            _trades.Add(_cycle.Close(fill, state));
            _cycle = null;
        }

        _last = state;
    }

    public void BarClosed(DateTimeOffset at, Candle candle, RunState state)
    {
        _metrics.BarClosed(at, candle, state);
        _closes.Add((at, state.Equity(candle.Close.Value)));
        _last = state;
    }

    public BacktestDetails Details()
    {
        IReadOnlyList<BacktestTrade> trades = _cycle is null ? _trades : [.. _trades, _cycle.Open()];

        return new BacktestDetails(_metrics.Metrics(), _fills, trades, Curve());
    }

    /// <summary>Closes in equal groups, so that there are no more than <see cref="MaxEquityPoints" /> of them.</summary>
    private IReadOnlyList<EquityPoint> Curve()
    {
        int size = (_closes.Count + MaxEquityPoints - 1) / MaxEquityPoints;

        return
        [
            .. _closes.Chunk(Math.Max(size, 1)).Select(group => new EquityPoint(group[^1].At, group[^1].Equity, group.Min(close => close.Equity)))
        ];
    }

    /// <summary>A position cycle being collected: entries and exits by quantity, fees in the quote asset.</summary>
    private sealed class Cycle(DateTimeOffset openedAt, RunState start)
    {
        private decimal _entryValue;
        private decimal _entryQuantity;
        private decimal _exitValue;
        private decimal _exitQuantity;
        private decimal _fees;

        public void Add(ReplayFill fill, StrategyDirection direction)
        {
            bool entry = (fill.Side == OrderSide.Buy) == (direction == StrategyDirection.Long);
            _fees += fill.FeeAsset == FeeAsset.Quote ? fill.Fee : fill.Fee * fill.Price;

            if (entry)
            {
                _entryValue += fill.Price * fill.Quantity;
                _entryQuantity += fill.Quantity;
            }
            else
            {
                _exitValue += fill.Price * fill.Quantity;
                _exitQuantity += fill.Quantity;
            }
        }

        /// <summary>The result is the cash the cycle returned over the cash it started with, with the dust it left at the last price.</summary>
        public BacktestTrade Close(ReplayFill fill, RunState state)
        {
            decimal pnl = state.Cash - start.Cash + state.DustValue(fill.Price) - start.DustValue(fill.Price);

            return new BacktestTrade(openedAt, fill.At, _entryValue / _entryQuantity, _exitValue / _exitQuantity, pnl, _fees);
        }

        public BacktestTrade Open()
        {
            return new BacktestTrade(openedAt, null, _entryValue / _entryQuantity, null, null, _fees);
        }
    }
}