namespace ProfitFox.Application.Exceptions;

/// <summary>
///     Bybit refused a call (change add-exchange-accounts, design.md, decision 6), with the reason Bybit gave; the API answers
///     502. The message never has the key, the secret or the whole answer of Bybit.
/// </summary>
public class ExchangeException(string message, Exception? inner = null) : Exception(message, inner);
