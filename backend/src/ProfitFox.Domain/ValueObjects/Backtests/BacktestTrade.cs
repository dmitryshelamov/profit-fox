namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     A position cycle from the fill that opened it to the fill that closed it; a trade open at the end has no close, exit and result.
///     Prices are averages of the fills weighted by quantity; <see cref="Fees" /> are in the quote asset.
/// </summary>
public sealed record BacktestTrade(
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    decimal EntryPrice,
    decimal? ExitPrice,
    decimal? Pnl,
    decimal Fees);