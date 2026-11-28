namespace ProfitFox.Application.Exceptions;

/// <summary>
///     Bybit did not answer, answered HTTP 403 or 5xx, or the limit of requests is hit (design.md, decision 6): the call may
///     be repeated later; the API answers 503.
/// </summary>
public sealed class ExchangeUnavailableException(string message, Exception? inner = null) : ExchangeException(message, inner);
