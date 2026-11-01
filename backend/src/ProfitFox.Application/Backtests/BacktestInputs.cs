using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.Backtests;

/// <summary>What every chunk of one backtest replays: the strategy version of the backtest and the stored candles of its period.</summary>
public sealed record BacktestInputs(Guid BacktestId, StrategySnapshot Snapshot, IReadOnlyList<Candle> Candles);