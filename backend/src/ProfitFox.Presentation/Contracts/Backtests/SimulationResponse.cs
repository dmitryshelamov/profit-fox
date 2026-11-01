namespace ProfitFox.Presentation.Contracts.Backtests;

public sealed record SimulationResponse(string Balance, string LimitFeePercent, string MarketFeePercent, string SlippagePercent, string LimitFillMode);