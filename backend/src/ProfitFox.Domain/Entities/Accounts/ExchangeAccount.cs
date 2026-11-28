namespace ProfitFox.Domain.Entities.Accounts;

/// <summary>
///     An account of the exchange Bybit the bench trades by (change add-exchange-accounts, spec exchange-accounts «Список
///     аккаунтов»): its name, one of its kind without regard to case, the UID Bybit named when the key was checked, the API key
///     in full and when it was added. The secret of the key is not part of it: only the store keeps it, encrypted.
/// </summary>
public sealed record ExchangeAccount(Guid Id, string Name, long Uid, string ApiKey, DateTimeOffset CreatedAt);
