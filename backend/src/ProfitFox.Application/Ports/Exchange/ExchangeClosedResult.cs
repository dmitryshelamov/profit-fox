namespace ProfitFox.Application.Ports.Exchange;

/// <summary>
///     What the exchange counted for the positions of a pair closed since a moment (<c>/v5/position/closed-pnl</c>): the result
///     after fees, the mean price of the exit, the quantity closed and when the last one closed.
/// </summary>
public sealed record ExchangeClosedResult(decimal Result, decimal ExitPrice, decimal Quantity, DateTimeOffset ClosedAt);
