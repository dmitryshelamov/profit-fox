using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.ValueObjects.Backtests;

namespace ProfitFox.Domain.Services.Backtests;

/// <summary>Collects the metrics of one combination while it runs, with no list of events kept.</summary>
public sealed class MetricsObserver(decimal balance) : IReplayObserver
{
    private readonly decimal _balance = balance;
    private RunState? _last;
    private decimal _lastClose;
    private decimal _endEquity = balance;
    private decimal _peak;
    private decimal _maxDrawdown;
    private int _fills;
    private decimal _fees;
    private decimal _maxPositionCost;
    private decimal _maxCapitalUsed;
    private int _candles;
    private int _inPosition;
    private int _underWater;
    private int _underWaterRun;
    private int _longestUnderWater;
    private int _closedTrades;
    private int _winningTrades;
    private RunState? _tradeStart;

    public void Filled(ReplayFill fill, RunState state)
    {
        RunState before = _last ?? state with { Cash = _balance, Quantity = 0m, Dust = 0m, DustCollateral = 0m };
        _fills++;
        _fees += fill.FeeAsset == FeeAsset.Quote ? fill.Fee : fill.Fee * fill.Price;

        if (before.Quantity == 0m && state.Quantity > 0m)
        {
            _tradeStart = before;
        }
        else if (before.Quantity > 0m && state.Quantity == 0m)
        {
            _closedTrades++;
            _winningTrades += state.Cash - _tradeStart!.Cash + state.DustValue(fill.Price) - _tradeStart.DustValue(fill.Price) > 0m ? 1 : 0;
        }

        Observe(state);
    }

    public void BarClosed(DateTimeOffset at, Candle candle, RunState state)
    {
        decimal close = candle.Close.Value;
        decimal equity = state.Equity(close);
        _candles++;
        _lastClose = close;
        _endEquity = equity;
        _peak = Math.Max(_peak, equity);
        _maxDrawdown = Math.Max(_maxDrawdown, (_peak - equity) / _peak * 100m);
        bool inPosition = state.Quantity > 0m;
        bool underWater = inPosition && state.Unrealized(close) < 0m;
        _inPosition += inPosition ? 1 : 0;
        _underWater += underWater ? 1 : 0;
        _underWaterRun = underWater ? _underWaterRun + 1 : 0;
        _longestUnderWater = Math.Max(_longestUnderWater, _underWaterRun);
        Observe(state);
    }

    public BacktestMetrics Metrics()
    {
        decimal pnl = _endEquity - _balance;
        decimal unrealized = _last is { Quantity: > 0m } state ? state.Unrealized(_lastClose) : 0m;
        decimal returnPercent = pnl / _balance * 100m;

        return new BacktestMetrics(
            _balance,
            _endEquity,
            pnl,
            pnl - unrealized,
            unrealized,
            returnPercent,
            _maxDrawdown,
            _maxDrawdown == 0m ? null : returnPercent / _maxDrawdown,
            _closedTrades,
            _closedTrades == 0 ? null : (decimal)_winningTrades / _closedTrades * 100m,
            _fills,
            _fees,
            _maxPositionCost,
            _maxPositionCost / _balance * 100m,
            _maxCapitalUsed,
            Share(_inPosition),
            Share(_underWater),
            _longestUnderWater,
            _last?.SkippedOrders ?? 0L,
            _candles);
    }

    private decimal Share(int closes)
    {
        return _candles == 0 ? 0m : (decimal)closes / _candles * 100m;
    }

    private void Observe(RunState state)
    {
        _maxPositionCost = Math.Max(_maxPositionCost, state.Invested);
        _maxCapitalUsed = Math.Max(_maxCapitalUsed, state.Invested + state.Reserved);
        _last = state;
    }
}