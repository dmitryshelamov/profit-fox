namespace ProfitFox.Application.Exceptions;

/// <summary>The exchange cannot serve requests for now: retries of a transient failure ran out or the circuit is open.</summary>
public class ExchangeUnavailableException(string message) : ExchangeException(message);