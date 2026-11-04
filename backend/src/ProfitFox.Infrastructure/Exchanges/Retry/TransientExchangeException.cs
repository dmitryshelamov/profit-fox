using ProfitFox.Application.Exceptions;

namespace ProfitFox.Infrastructure.Exchanges.Retry;

/// <summary>
///     An exchange failure worth retrying: rate limit, server error, timeout or no connection. <see cref="RateLimited" /> says the
///     exchange said the requests went over its rate limit or banned the IP for it.
/// </summary>
internal sealed class TransientExchangeException(string message, bool rateLimited = false) : Exception(message)
{
    public bool RateLimited { get; } = rateLimited;

    /// <summary>What the caller is told once no more attempts are made.</summary>
    public ExchangeUnavailableException Unavailable(string message)
    {
        return RateLimited ? new ExchangeRateLimitedException(message) : new ExchangeUnavailableException(message);
    }
}