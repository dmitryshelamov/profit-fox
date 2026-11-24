namespace ProfitFox.Application.Ports.MarketData;

/// <summary>
///     The steps of a read of recent history for the page of a pair (change add-ideas-and-market, task 23.2): a client that
///     reads in several attempts — the open interest from History, then the REST — runs each under the limit of the page.
///     The scanner and the backtests read without it.
/// </summary>
public interface IReadSteps
{
    /// <summary>
    ///     Runs one attempt of <paramref name="step" /> with a token cancelled when the request is or the limit expires.
    /// </summary>
    /// <exception cref="Exceptions.ReadTimedOutException">The attempt did not end within the limit.</exception>
    /// <exception cref="OperationCanceledException">The request was cancelled.</exception>
    Task<T> Run<T>(string step, Func<CancellationToken, Task<T>> attempt, CancellationToken cancellation);
}
