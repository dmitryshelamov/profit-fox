using System.Globalization;

using ProfitFox.Application.Ports.ExchangeAccounts;

namespace ProfitFox.Presentation.Contracts.ExchangeAccounts;

/// <summary>Amounts are decimal strings in <c>currency</c> (<c>USD</c> on Bybit, <c>RUB</c> on T-Invest): JSON numbers lose precision in JavaScript clients.</summary>
public sealed record AccountBalanceResponse(string Currency, string TotalEquity, IReadOnlyList<CoinBalanceResponse> Coins)
{
    public static AccountBalanceResponse Of(AccountBalance balance)
    {
        return new AccountBalanceResponse(
            balance.Currency,
            balance.TotalEquity.ToString(CultureInfo.InvariantCulture),
            [
                .. balance.Coins.Select(coin => new CoinBalanceResponse(
                    coin.Coin,
                    coin.WalletBalance.ToString(CultureInfo.InvariantCulture),
                    coin.Value.ToString(CultureInfo.InvariantCulture)))
            ]);
    }
}