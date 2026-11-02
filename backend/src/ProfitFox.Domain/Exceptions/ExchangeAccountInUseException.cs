namespace ProfitFox.Domain.Exceptions;

/// <summary>An exchange account cannot be removed because a bot trades with it.</summary>
public sealed class ExchangeAccountInUseException() : Exception("A bot refers to the exchange account.");