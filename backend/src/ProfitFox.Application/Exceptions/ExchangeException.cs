namespace ProfitFox.Application.Exceptions;

/// <summary>The exchange rejected a request or could not serve it.</summary>
public class ExchangeException(string message) : BaseApplicationException(message);