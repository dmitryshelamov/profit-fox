namespace ProfitFox.Application.Exceptions;

/// <summary>The exchange says the requests went over its rate limit (HTTP 429, code 10006) or bans the IP for it (HTTP 403).</summary>
public sealed class ExchangeRateLimitedException(string message) : ExchangeUnavailableException(message);