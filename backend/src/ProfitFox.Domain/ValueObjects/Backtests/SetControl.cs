using ProfitFox.Domain.Services.Runs;

namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     What a set needs on a pair for its random entries in the second pass (change fix-backtest-random-control, design.md,
///     decisions 2 and 4): its orders over every pair and the control groups of the weeks of its timeframe.
/// </summary>
public sealed record SetControl(SetOrders Orders, ControlGroups Groups);