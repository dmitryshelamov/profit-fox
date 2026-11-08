namespace ProfitFox.Application.Ports.ExchangeAccounts;

/// <summary>
///     The balance of an account as the exchange reports it now: the currency the values are in (<c>USD</c> on Bybit, <c>RUB</c> on
///     T-Invest), the total equity and every coin or share it holds, with its value in that currency.
/// </summary>
public sealed record AccountBalance(string Currency, decimal TotalEquity, IReadOnlyList<CoinBalance> Coins);