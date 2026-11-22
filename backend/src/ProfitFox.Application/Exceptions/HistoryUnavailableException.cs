namespace ProfitFox.Application.Exceptions;

/// <summary>
///     The market-data service did not give the history: a backtest stops failed, without numbers.
/// </summary>
/// <param name="message">What the service answered.</param>
/// <param name="inner">The error of the call.</param>
/// <param name="dropped">
///     The service dropped the read or did not take it — it is out, slow or broke the stream — so the same read may give the
///     history when it is repeated; a refusal of the window is not dropped (task 12.12).
/// </param>
public sealed class HistoryUnavailableException(string message, Exception? inner = null, bool dropped = false) : Exception(message, inner)
{
    /// <summary>
    ///     The read was dropped or not taken by the service, not refused: repeating it may give the history.
    /// </summary>
    public bool Dropped { get; } = dropped;
}
