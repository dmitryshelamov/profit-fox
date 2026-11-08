namespace ProfitFox.Presentation.Contracts.ExchangeAccounts;

/// <summary>A coin or a share the account holds and its value in the currency of the balance.</summary>
public sealed record CoinBalanceResponse(string Coin, string WalletBalance, string Value);