namespace ProfitFox.Application.Exceptions;

/// <summary>
///     There is no such backtest, or no such set of it.
/// </summary>
public sealed class BacktestNotFoundException(string message) : Exception(message);
