namespace ProfitFox.Presentation.Contracts.ExchangeAccounts;

public sealed record CoinBalanceResponse(string Coin, string WalletBalance, string UsdValue);