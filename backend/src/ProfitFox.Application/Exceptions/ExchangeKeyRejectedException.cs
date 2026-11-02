namespace ProfitFox.Application.Exceptions;

/// <summary>The exchange does not accept an API key: it is wrong, expired, deleted or not allowed from this address.</summary>
public sealed class ExchangeKeyRejectedException(string message) : ExchangeException(message);