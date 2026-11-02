using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>A fill of an order in a result's details; <see cref="At" /> is the open time of the candle.</summary>
public sealed record BacktestFill(DateTimeOffset At, decimal Price, decimal Quantity, decimal Fee, FeeAsset FeeAsset);