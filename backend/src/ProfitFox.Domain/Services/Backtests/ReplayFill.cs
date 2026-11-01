using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Domain.Services.Backtests;

/// <summary>A fill the simulator gave the strategy; <see cref="At" /> is the open time of the candle.</summary>
public sealed record ReplayFill(DateTimeOffset At, string OrderId, OrderSide Side, OrderType Type, decimal Price, decimal Quantity, decimal Fee, FeeAsset FeeAsset);