using System.Net;

using Bybit.Net;
using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.V5;
using Bybit.Net.Objects.Options;

using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Errors;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.Exchange;
using ProfitFox.Domain.ValueObjects.Accounts;

namespace ProfitFox.Infrastructure.Clients.Bybit;

/// <summary>
///     The private calls of Bybit v5 with the key of an account (change add-exchange-accounts, design.md, decisions 4, 6 and
///     7): <c>/v5/user/query-api</c> for what the key may do and <c>/v5/account/wallet-balance</c> for the free money, in the
///     cross and in the isolated margin. A
///     client of <c>Bybit.Net</c> is built over the typed <see cref="HttpClient" /> for each call with the key of that call and
///     dropped with it, so the secret stays in no cache. One attempt per call: the owner waits for the answer.
/// </summary>
/// <remarks>
///     The client of the package gets no journal: its records could carry the request, and the answer of <c>query-api</c>
///     carries the secret. This class writes the refusal itself — the call, the code and the reason of Bybit, nothing else.
/// </remarks>
internal sealed class BybitAccountClient(HttpClient http, BybitSettings settings, ILogger<BybitAccountClient>? logger = null) : IExchangeKeyInspector
{
    // The codes of a key Bybit does not take: invalid key, wrong signature, no permission, IP not allowed, key expired.
    private static readonly HashSet<string> RejectedKeyCodes = ["10003", "10004", "10005", "10010", "33004"];

    // The code of the limit of requests of Bybit.
    private const string RateLimitCode = "10006";

    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

    public async Task<ExchangeKeyInfo> InspectAsync(string apiKey, string secret, CancellationToken cancellationToken)
    {
        using BybitRestClient client = Client(apiKey, secret);
        BybitApiKeyInfo key = Unwrap("query-api", await client.V5Api.Account.GetApiKeyInfoAsync(cancellationToken));
        return new ExchangeKeyInfo(
            key.UserId,
            key.Readonly,
            key.Uta,
            [.. key.Permissions?.Wallet ?? []],
            [.. key.Permissions?.ContractTrade ?? []]);
    }

    public async Task<decimal> FreeAsync(string apiKey, string secret, CancellationToken cancellationToken)
    {
        using BybitRestClient client = Client(apiKey, secret);
        BybitResponse<BybitBalance> balances = Unwrap("wallet-balance", await client.V5Api.Account.GetBalancesAsync(AccountType.Unified, ct: cancellationToken));
        return Free(balances.List.SingleOrDefault());
    }

    /// <summary>
    ///     The free money of the unified wallet (design.md, decision 7): the available balance of the whole account when Bybit
    ///     sends it — the cross and portfolio margin. In the isolated margin Bybit sends the fields of the whole account empty,
    ///     and the free money is that of USDT by the formula of Bybit for it: <c>walletBalance - totalPositionIM -
    ///     totalOrderIM - locked - bonus</c>, an empty part nought. No unified wallet, or no USDT in it, is no money for the
    ///     USDT perpetuals. An empty value is never shown as nought: USDT without its balance or without both margins is a
    ///     refusal.
    /// </summary>
    /// <exception cref="ExchangeException">USDT is there without the numbers to count from.</exception>
    private decimal Free(BybitBalance? wallet)
    {
        if (wallet?.TotalAvailableBalance is { } available)
        {
            return available;
        }

        BybitAssetBalance? usdt = wallet?.Assets?.FirstOrDefault(asset => asset.Asset == "USDT");
        if (usdt is null)
        {
            return 0m;
        }

        if (usdt.WalletBalance is not { } balance || (usdt.TotalPositionInitialMargin is null && usdt.TotalOrderInitialMargin is null))
        {
            _logger.LogWarning("Bybit sent no free money of the unified account: USDT without its balance or margins");
            throw new ExchangeException("Bybit не прислал свободные деньги аккаунта");
        }

        return balance - (usdt.TotalPositionInitialMargin ?? 0m) - (usdt.TotalOrderInitialMargin ?? 0m) - (usdt.Locked ?? 0m) - (usdt.Bonus ?? 0m);
    }

    private BybitRestClient Client(string apiKey, string secret)
    {
        return new BybitRestClient(
            http,
            NullLoggerFactory.Instance,
            Options.Create(new BybitRestOptions { ApiCredentials = new BybitCredentials(apiKey, secret), Environment = settings.Environment() }));
    }

    /// <summary>
    ///     The data of a call, or the refusal of design.md, decision 6.
    /// </summary>
    private T Unwrap<T>(string call, HttpResult<T> result)
    {
        if (result.Success)
        {
            return result.Data!;
        }

        Error error = result.Error!;
        string reason = string.IsNullOrWhiteSpace(error.Message) ? $"Bybit refused the call {call}" : error.Message;
        _logger.LogWarning("Bybit refused {Call}: HTTP {Status}, code {Code}, {Reason}", call, (int?)result.ResponseStatusCode, error.ErrorCode, reason);

        if (error.ErrorCode is { } code && RejectedKeyCodes.Contains(code))
        {
            throw new ExchangeKeyRejectedException(reason);
        }

        // No answer at all (the connection, the time limit), a 5xx, the limit of requests (HTTP 429 or 10006) or a ban of the
        // address for it (HTTP 403).
        if (result.ResponseStatusCode is null or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError
            || error.ErrorCode == RateLimitCode
            || error.ErrorType is ErrorType.RateLimitRequest)
        {
            throw new ExchangeUnavailableException($"Bybit is unavailable: {reason}");
        }

        throw new ExchangeException(reason);
    }
}
