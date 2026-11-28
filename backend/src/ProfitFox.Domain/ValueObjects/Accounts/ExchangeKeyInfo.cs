namespace ProfitFox.Domain.ValueObjects.Accounts;

/// <summary>
///     What Bybit says of an API key (change add-exchange-accounts, design.md, decision 5): the UID of its account, whether the
///     key is read-only, whether the account is a unified trading account, and the permissions of the groups the rules look at
///     — <c>Wallet</c> and <c>ContractTrade</c> — as Bybit names them.
/// </summary>
public sealed record ExchangeKeyInfo(long Uid, bool ReadOnly, bool Unified, IReadOnlyList<string> Wallet, IReadOnlyList<string> ContractTrade);
