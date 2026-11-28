namespace ProfitFox.Domain.Entities.Accounts;

/// <summary>
///     An account of the exchange Bybit the bench trades by (change add-exchange-accounts, spec exchange-accounts «Список
///     аккаунтов»): its name, one of its kind without regard to case, the UID Bybit named when the key was checked, the API key
///     in full and when it was added. The secret of the key is not part of it: only the store keeps it, encrypted. The margin
///     mode is the one Bybit named when it was last read — at the addition or at «Показать» — as Bybit names it, null when
///     none was read (design.md, decision 11). How the trading Worker reads it (change take-idea-trades, design.md, decision 3):
///     by the stream or by polling — null while it has no trades —, since when, and from which moment it reads the fills by REST.
/// </summary>
public sealed record ExchangeAccount(
    Guid Id,
    string Name,
    long Uid,
    string ApiKey,
    DateTimeOffset CreatedAt,
    string? MarginMode = null,
    string? Feed = null,
    DateTimeOffset? FeedChangedAt = null,
    DateTimeOffset? FillsCursorAt = null);
