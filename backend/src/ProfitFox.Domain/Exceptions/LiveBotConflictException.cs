namespace ProfitFox.Domain.Exceptions;

/// <summary>An action with a bot that its status does not allow, such as starting a running bot.</summary>
public sealed class LiveBotConflictException(string message) : Exception(message);