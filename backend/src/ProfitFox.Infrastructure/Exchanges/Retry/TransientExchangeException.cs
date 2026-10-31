namespace ProfitFox.Infrastructure.Exchanges.Retry;

/// <summary>An exchange failure worth retrying: rate limit, server error, timeout or no connection.</summary>
internal sealed class TransientExchangeException(string message) : Exception(message);