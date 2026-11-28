namespace ProfitFox.Domain.Exceptions;

/// <summary>
///     A Worker wanted to write under a lease it no longer holds: another Worker has taken it over, or it has ended. Nothing
///     is written. The lease is that of a backtest, or that of the trading Worker (change take-idea-trades, design.md,
///     decision 2).
/// </summary>
public sealed class LeaseLostException : Exception
{
    public LeaseLostException(string runId, string worker)
        : base($"Worker {worker} no longer holds the lease of run {runId}.")
    {
    }

    private LeaseLostException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     The trading Worker <paramref name="worker" /> no longer holds the lease of the trader.
    /// </summary>
    public static LeaseLostException OfTrader(string worker)
    {
        return new LeaseLostException($"Worker {worker} no longer holds the lease of the trader.");
    }
}
