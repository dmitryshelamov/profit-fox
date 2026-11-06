namespace ProfitFox.Domain.Exceptions;

/// <summary>An action with a trade by an idea that its status does not allow, such as cancelling a trade that is open.</summary>
public sealed class IdeaTradeConflictException(string message) : Exception(message);