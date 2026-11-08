using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.V5;

using CryptoExchange.Net.Objects;

using Microsoft.Extensions.Logging;

using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Infrastructure.Exchanges.Bybit;

/// <summary>
///     Private Bybit v5 calls with the key of an account: what the key may do and the balance. A client is built for each call and
///     dropped with it, so the secret stays in no cache. One attempt per call, as the user waits for the answer; never sends orders.
/// </summary>
internal sealed class BybitAccountClient(HttpClient http, ILoggerFactory loggers, BybitSettings settings) : IExchangeKeyInspector
{
    public async Task<ExchangeKeyInfo> InspectAsync(string apiKey, string secret, CancellationToken cancellationToken)
    {
        using BybitRestClient client = Client(apiKey, secret);
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

    public async Task<AccountBalance> GetBalanceAsync(string apiKey, string secret, CancellationToken cancellationToken)
    {
        using BybitRestClient client = Client(apiKey, secret);
        BybitBalance? balance = Unwrap(await client.V5Api.Account.GetBalancesAsync(AccountType.Unified, ct: cancellationToken)).List.SingleOrDefault();

        // An account Bybit lists no unified wallet for holds nothing.
        if (balance is null)
        {
            return new AccountBalance("USD", 0m, []);
        }

        return new AccountBalance(
            "USD",
            balance.TotalEquity ?? 0m,
            [
                .. balance.Assets
                    .Where(asset => asset.WalletBalance is not (null or 0m))
                    .OrderByDescending(asset => asset.UsdValue)
                    .Select(asset => new CoinBalance(asset.Asset, asset.WalletBalance!.Value, asset.UsdValue ?? 0m))
            ]);
    }

    private BybitRestClient Client(string apiKey, string secret)
    {
        return BybitKeyClients.Create(http, loggers, settings, apiKey, secret);
    }

    private static bool Has(IEnumerable<string>? permissions, string permission)
    {
        return permissions?.Contains(permission, StringComparer.Ordinal) == true;
    }

    private static T Unwrap<T>(HttpResult<T> result)
    {
        return BybitKeyClients.Unwrap(result);
    }
}