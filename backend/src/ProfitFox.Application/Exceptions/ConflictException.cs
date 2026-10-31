namespace ProfitFox.Application.Exceptions;

/// <summary>The request does not fit the current state of the item; nothing was changed.</summary>
public sealed class ConflictException(string message) : BaseApplicationException(message);