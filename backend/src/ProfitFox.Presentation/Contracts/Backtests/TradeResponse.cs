namespace ProfitFox.Presentation.Contracts.Backtests;

/// <summary>A position cycle; one open at the end has no close, exit price or result. Fees are in the quote asset.</summary>
public sealed record TradeResponse(DateTimeOffset OpenedAt, DateTimeOffset? ClosedAt, string EntryPrice, string? ExitPrice, string? Pnl, string Fees);