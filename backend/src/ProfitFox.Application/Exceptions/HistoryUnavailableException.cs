namespace ProfitFox.Application.Exceptions;

/// <summary>
///     The market-data service did not give the history: a backtest stops failed, without numbers.
/// </summary>
public sealed class HistoryUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
