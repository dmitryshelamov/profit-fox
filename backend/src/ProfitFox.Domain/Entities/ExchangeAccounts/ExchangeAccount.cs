using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Domain.Entities.ExchangeAccounts;

/// <summary>
///     Keys of one account on the main network of the exchange, which a bot will trade with. The secret is kept apart, encrypted, and is read only to
///     call the exchange. <see cref="Uid" /> and <see cref="Categories" /> are what the exchange told about the key when it was added.
/// </summary>
public sealed record ExchangeAccount(
    Guid Id,
    string Name,
    string ApiKey,
    long Uid,
    IReadOnlySet<MarketCategory> Categories,
    DateTimeOffset CreatedAt);