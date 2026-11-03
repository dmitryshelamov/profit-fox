using System.Net;

using Bybit.Net;
using Bybit.Net.Clients;
using Bybit.Net.Objects.Options;

using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Errors;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitFox.Application.Exceptions;

namespace ProfitFox.Infrastructure.Exchanges.Bybit;

/// <summary>
///     Bybit clients with the key of an account, one per call and dropped with it, so the secret stays in no cache; and what their
///     answers mean.
/// </summary>
internal static class BybitKeyClients
{
    // Codes of a key Bybit does not accept: invalid, bad signature, no permission, IP not allowed, expired.
    private static readonly HashSet<string> RejectedKeyCodes = ["10003", "10004", "10005", "10010", "33004"];

    public static BybitRestClient Create(HttpClient http, ILoggerFactory loggers, BybitSettings settings, string apiKey, string secret)
    {
        return new BybitRestClient(
            http,
            loggers,
            Options.Create(new BybitRestOptions { ApiCredentials = new BybitCredentials(apiKey, secret), Environment = settings.Environment() }));
    }

    /// <summary>A failure of the key is <see cref="ExchangeKeyRejectedException" />, no answer <see cref="ExchangeUnavailableException" />.</summary>
    public static T Unwrap<T>(HttpResult<T> result)
    {
        if (result.Success)
        {
            return result.Data!;
        }

        // Only the code and the message of Bybit: the request carries the key, so it never goes into a message.
        if (result.Error!.ErrorCode is { } code && RejectedKeyCodes.Contains(code))
        {
            throw new ExchangeKeyRejectedException($"Bybit does not accept the key: {result.Error.Message}");
        }

        // No answer at all (connection, timeout), a 5xx, the rate limit (HTTP 429 or retCode 10006) or a ban of the IP for it (HTTP 403).
        if (result.ResponseStatusCode is null or HttpStatusCode.Forbidden or >= HttpStatusCode.InternalServerError || result.Error.ErrorType is ErrorType.RateLimitRequest)
        {
            throw new ExchangeUnavailableException($"Bybit is unavailable: {result.Error.Message}");
        }

        throw new ExchangeException(result.Error.Message!);
    }
}