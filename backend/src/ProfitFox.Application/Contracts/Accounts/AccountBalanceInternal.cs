namespace ProfitFox.Application.Contracts.Accounts;

/// <summary>
///     What «Показать» reads at Bybit (spec exchange-accounts «Баланс по запросу», «Режим маржи аккаунта»): the free money of
///     the unified account in USDT and its margin mode now, as Bybit names it.
/// </summary>
public sealed record AccountBalanceInternal(decimal Free, string? MarginMode);
