namespace ProfitFox.Application.Exceptions;

/// <summary>The requested item does not exist.</summary>
public sealed class NotFoundException(string message) : BaseApplicationException(message);