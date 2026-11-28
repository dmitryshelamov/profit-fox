namespace ProfitFox.Application.Exceptions;

/// <summary>
///     «Разобрана» cannot check the position at Bybit (review of the application, R1): Bybit no longer takes the key of the
///     account, or its secret does not read. The owner may confirm that the position on Bybit is closed and resolve the trade
///     without the check; the API answers 409 and says so. Nothing was written.
/// </summary>
public sealed class TradeUncheckedException(string reason, Exception? inner = null) : Exception(reason, inner);
