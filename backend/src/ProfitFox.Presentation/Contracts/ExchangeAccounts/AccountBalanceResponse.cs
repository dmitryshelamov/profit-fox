using System.Globalization;

using ProfitFox.Application.Ports.ExchangeAccounts;

namespace ProfitFox.Presentation.Contracts.ExchangeAccounts;

/// <summary>Amounts are decimal strings: JSON numbers lose precision in JavaScript clients.</summary>
public sealed record AccountBalanceResponse(string TotalEquityUsd, IReadOnlyList<CoinBalanceResponse> Coins)
{
    public static AccountBalanceResponse Of(AccountBalance balance)
    {
        return new AccountBalanceResponse(
            balance.TotalEquityUsd.ToString(CultureInfo.InvariantCulture),
            [
                .. balance.Coins.Select(coin => new CoinBalanceResponse(
                    coin.Coin,
                    coin.WalletBalance.ToString(CultureInfo.InvariantCulture),
                    coin.UsdValue.ToString(CultureInfo.InvariantCulture)))
            ]);
    }
}