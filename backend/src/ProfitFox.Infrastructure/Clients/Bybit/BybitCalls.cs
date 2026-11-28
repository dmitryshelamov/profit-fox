using System.Net;

using Bybit.Net;
using Bybit.Net.Clients;
using Bybit.Net.Objects.Options;

using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Errors;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.Exchange;

namespace ProfitFox.Infrastructure.Clients.Bybit;

/// <summary>
///     What the clients of the trades share (change take-idea-trades, design.md, decision 4): a client of <c>Bybit.Net</c>
///     built for one call over the typed <see cref="HttpClient" /> and dropped with it, a read unwrapped or turned into the
///     refusal of change add-exchange-accounts, and a write turned into an <see cref="ExchangeAnswer" /> by the table of
///     decision 4. The journal gets the call, the HTTP status, the code and the reason of Bybit, and at the level of debug the
///     raw answer — an answer of these calls carries no key, secret or signature; the request is never written.
/// </summary>
internal sealed class BybitCalls(HttpClient http, BybitSettings settings, ILogger? logger)
{
    // The codes of a key Bybit does not take: invalid key, wrong signature, no permission, IP not allowed, key expired.
    private static readonly HashSet<string> RejectedKeyCodes = ["10003", "10004", "10005", "10010", "33004"];

    // For now (design.md, decision 4): a server error, the limit of requests, a timeout of the server.
    private static readonly HashSet<string> TemporaryCodes = ["10000", "10006", "10016"];

    private const string DuplicateCode = "110072";
    private const string UnchangedCode = "110043";

    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    public BybitRestClient Client(ExchangeCredentials? credentials)
    {
        return new BybitRestClient(
            http,
            NullLoggerFactory.Instance,
            Options.Create(new BybitRestOptions
            {
                ApiCredentials = credentials is null ? null : new BybitCredentials(credentials.ApiKey, credentials.Secret),
                Environment = settings.Environment(),
                OutputOriginalData = true
            }));
    }

    /// <summary>
    ///     The data of a read, or the refusal of change add-exchange-accounts, design.md, decision 6.
    /// </summary>
    public T Read<T>(string call, HttpResult<T> result)
    {
        Raw(call, result);
        if (result.Success)
        {
            return result.Data!;
        }

        Error error = result.Error!;
        string reason = Reason(call, error);
        _logger.LogWarning("Bybit refused {Call}: HTTP {Status}, code {Code}, {Reason}", call, (int?)result.ResponseStatusCode, error.ErrorCode, reason);
        if (error.ErrorCode is { } code && RejectedKeyCodes.Contains(code))
        {
            throw new ExchangeKeyRejectedException(reason);
        }

        if (Temporary(result.ResponseStatusCode, error))
        {
            throw new ExchangeUnavailableException($"Bybit is unavailable: {reason}");
        }

        throw new ExchangeException(reason);
    }

    /// <summary>
    ///     A write by the table of design.md, decision 4.
    /// </summary>
    /// <exception cref="ExchangeKeyRejectedException">Bybit does not take the key.</exception>
    public ExchangeAnswer Write(string call, HttpResult result, string? orderId = null)
    {
        Raw(call, result);
        if (result.Success)
        {
            return new ExchangeAnswer(ExchangeAnswerKind.Accepted, OrderId: orderId);
        }

        Error error = result.Error!;
        string reason = Reason(call, error);
        _logger.LogWarning("Bybit refused {Call}: HTTP {Status}, code {Code}, {Reason}", call, (int?)result.ResponseStatusCode, error.ErrorCode, reason);
        if (error.ErrorCode is { } code && RejectedKeyCodes.Contains(code))
        {
            throw new ExchangeKeyRejectedException(reason);
        }

        return error.ErrorCode switch
        {
            DuplicateCode => new ExchangeAnswer(ExchangeAnswerKind.Duplicate, DuplicateCode, reason),
            UnchangedCode => new ExchangeAnswer(ExchangeAnswerKind.Unchanged, UnchangedCode, reason),
            _ when Temporary(result.ResponseStatusCode, error) => new ExchangeAnswer(ExchangeAnswerKind.Temporary, error.ErrorCode, reason),
            _ => new ExchangeAnswer(ExchangeAnswerKind.Refused, error.ErrorCode, reason)
        };
    }

    // No answer at all (the connection, the time limit), a 5xx, the limit of requests (HTTP 429 or 10006), a ban of the
    // address for it (HTTP 403), or a code of Bybit that is for now.
    private static bool Temporary(HttpStatusCode? status, Error error)
    {
        return status is null or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError
               || error.ErrorCode is { } code && TemporaryCodes.Contains(code)
               || error.ErrorType is ErrorType.RateLimitRequest;
    }

    private static string Reason(string call, Error error)
    {
        return string.IsNullOrWhiteSpace(error.Message) ? $"Bybit refused the call {call}" : error.Message;
    }

    private void Raw(string call, HttpResult result)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Bybit answered {Call}: HTTP {Status}, {Answer}", call, (int?)result.ResponseStatusCode, result.OriginalData);
        }
    }
}
