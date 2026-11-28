namespace ProfitFox.Application.Exceptions;

/// <summary>
///     Bybit does not take the key or its secret (design.md, decision 6: <c>retCode</c> 10003, 10004, 10005, 10010, 33004);
///     the message is the reason of Bybit, which the API tells at the field of the key.
/// </summary>
public sealed class ExchangeKeyRejectedException(string message) : ExchangeException(message);
