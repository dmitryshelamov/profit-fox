using ProfitFox.Domain.Entities.Backtests.Enums;

namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>The simulation settings a new backtest asks for; a missing one takes its default.</summary>
public sealed record SimulationRequest(decimal? Balance, decimal? LimitFeePercent, decimal? MarketFeePercent, decimal? SlippagePercent, LimitFillMode? LimitFillMode);