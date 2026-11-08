namespace ProfitFox.Application.Ports.ExchangeAccounts;

/// <summary>A coin or a share the account holds: how many, and what they are worth in the currency of the balance.</summary>
public sealed record CoinBalance(string Coin, decimal WalletBalance, decimal Value);