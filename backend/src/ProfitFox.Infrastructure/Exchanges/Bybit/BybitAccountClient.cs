using System.Net;

using Bybit.Net;
using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.V5;
using Bybit.Net.Objects.Options;

using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Errors;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Infrastructure.Exchanges.Bybit;

/// <summary>
///     Private Bybit v5 calls with the key of an account: what the key may do and the balance. A client is built for each call and
///     dropped with it, so the secret stays in no cache. One attempt per call, as the user waits for the answer; never sends orders.
/// </summary>
internal sealed class BybitAccountClient(HttpClient http, ILoggerFactory loggers, BybitSettings settings) : IExchangeKeyInspector
{
    // Codes of a key Bybit does not accept: invalid, bad signature, no permission, IP not allowed, expired.
    private static readonly HashSet<string> RejectedKeyCodes = ["10003", "10004", "10005", "10010", "33004"];

    public async Task<ExchangeKeyInfo> InspectAsync(ExchangeEnvironment environment, string apiKey, string secret, CancellationToken cancellationToken)
    {
        using BybitRestClient client = Client(environment, apiKey, secret);
        BybitApiKeyInfo key = Unwrap(await client.V5Api.Account.GetApiKeyInfoAsync(cancellationToken));
        var categories = new HashSet<MarketCategory>();

        if (Has(key.Permissions.Spot, "SpotTrade"))
        {
            categories.Add(MarketCategory.Spot);
        }

        if (Has(key.Permissions.ContractTrade, "Order") && Has(key.Permissions.ContractTrade, "Position"))
        {
            categories.Add(MarketCategory.Linear);
        }

        return new ExchangeKeyInfo(key.UserId, key.Readonly, key.Uta, Has(key.Permissions.Wallet, "Withdraw"), categories);
    }

    public async Task<AccountBalance> GetBalanceAsync(ExchangeEnvironment environment, string apiKey, string secret, CancellationToken cancellationToken)
    {
        using BybitRestClient client = Client(environment, apiKey, secret);
        BybitBalance? balance = Unwrap(await client.V5Api.Account.GetBalancesAsync(AccountType.Unified, ct: cancellationToken)).List.SingleOrDefault();

        // An account Bybit lists no unified wallet for holds nothing.
        if (balance is null)
        {
            return new AccountBalance(0m, []);
        }

        return new AccountBalance(
            balance.TotalEquity ?? 0m,
            [
                .. balance.Assets
                    .Where(asset => asset.WalletBalance is not (null or 0m))
                    .OrderByDescending(asset => asset.UsdValue)
                    .Select(asset => new CoinBalance(asset.Asset, asset.WalletBalance!.Value, asset.UsdValue ?? 0m))
            ]);
    }

    private BybitRestClient Client(ExchangeEnvironment environment, string apiKey, string secret)
    {
        return new BybitRestClient(
            http,
            loggers,
            Options.Create(new BybitRestOptions { ApiCredentials = new BybitCredentials(apiKey, secret), Environment = Environment(environment) }));
    }

    private BybitEnvironment Environment(ExchangeEnvironment environment)
    {
        if (!string.IsNullOrEmpty(settings.RestAddress))
        {
            return BybitEnvironment.CreateCustom("profit-fox", settings.RestAddress, BybitEnvironment.Live.SocketBaseAddress);
        }

        return environment == ExchangeEnvironment.Mainnet ? BybitEnvironment.Live : BybitEnvironment.Testnet;
    }

    private static bool Has(IEnumerable<string>? permissions, string permission)
    {
        return permissions?.Contains(permission, StringComparer.Ordinal) == true;
    }

    /// <summary>A failure of the key is <see cref="ExchangeKeyRejectedException" />, no answer <see cref="ExchangeUnavailableException" />.</summary>
    private static T Unwrap<T>(HttpResult<T> result)
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

        // No answer at all (connection, timeout), a 5xx or the rate limit (HTTP 429 or retCode 10006).
        if (result.ResponseStatusCode is null or >= HttpStatusCode.InternalServerError || result.Error.ErrorType is ErrorType.RateLimitRequest)
        {
            throw new ExchangeUnavailableException($"Bybit is unavailable: {result.Error.Message}");
        }

        throw new ExchangeException(result.Error.Message!);
    }
}