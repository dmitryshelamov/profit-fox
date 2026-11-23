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
/// <param name="reason">
///     What the refusal is without what changes from one read to the next — the code and what was asked, without the body of
///     the answer: the scanner tells one reason once (task 19.4); the message when not given.
/// </param>
public sealed class HistoryUnavailableException(string message, Exception? inner = null, bool dropped = false, string? reason = null) : Exception(message, inner)
{
    /// <summary>
    ///     What the refusal is without the body of the answer: refusals alike have one reason whatever their bodies.
    /// </summary>
    public string Reason { get; } = reason ?? message;

    /// <summary>
    ///     The read was dropped or not taken by the service, not refused: repeating it may give the history.
    /// </summary>
    public bool Dropped { get; } = dropped;
}
