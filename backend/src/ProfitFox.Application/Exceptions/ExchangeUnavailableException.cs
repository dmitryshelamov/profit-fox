namespace ProfitFox.Application.Exceptions;

/// <summary>The exchange cannot serve requests for now: retries of a transient failure ran out or the circuit is open.</summary>
public sealed class ExchangeUnavailableException(string message) : ExchangeException(message);