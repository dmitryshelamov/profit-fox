namespace ProfitFox.Application.Ports.ExchangeAccounts;

/// <summary>The balance of an account as the exchange reports it now: the total equity and every coin it holds, in USD as well.</summary>
public sealed record AccountBalance(decimal TotalEquityUsd, IReadOnlyList<CoinBalance> Coins);