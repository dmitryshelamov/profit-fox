namespace ProfitFox.Application.Exceptions;

/// <summary>
///     A read of the page of a pair did not end within the limit of its attempt (change add-ideas-and-market, task 23.2):
///     the page is answered that its candles or its open interest load, and a catalog out of time is a refusal. Not a
///     <see cref="HistoryUnavailableException" />: the client of the open interest asks History again a day earlier after a
///     refusal (task 20.7), not after a read out of time.
/// </summary>
/// <param name="step">The step out of time: <c>catalog</c>, <c>candles</c>, <c>funding</c>, <c>oi-history</c>, <c>oi-rest</c>.</param>
/// <param name="limit">The limit of the attempt.</param>
/// <param name="inner">What the read ended with when the limit cancelled it.</param>
public sealed class ReadTimedOutException(string step, TimeSpan limit, Exception? inner = null)
    : Exception($"The read of {step} of the page of the pair did not end in {(long)limit.TotalSeconds} s.", inner)
{
    /// <summary>
    ///     The step out of time.
    /// </summary>
    public string Step { get; } = step;
}
