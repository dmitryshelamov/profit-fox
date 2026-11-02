namespace ProfitFox.Application.Ports.ExchangeAccounts;

public sealed record CoinBalance(string Coin, decimal WalletBalance, decimal UsdValue);