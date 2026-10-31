namespace ProfitFox.Application.Exceptions;

public sealed class NotFoundException(string message) : BaseApplicationException(message);