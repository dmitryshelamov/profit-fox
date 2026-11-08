using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Domain.Entities.ExchangeAccounts;

/// <summary>
///     An account on an exchange, which a bot will trade with: on Bybit the key of one account of the main network, with its
///     <see cref="Uid" />; on T-Invest the brokerage account <see cref="AccountId" /> a token opens. The secret (the API secret of Bybit,
///     the token of T-Invest) is kept apart, encrypted, and is read only to call the exchange. <see cref="Categories" /> are the markets
///     the account trades in: what Bybit told about the key when it was added, <c>moex</c> for T-Invest.
/// </summary>
public sealed record ExchangeAccount
{
    public Guid Id { get; }

    public string Name { get; init; }

    public Exchange Exchange { get; }

    /// <summary>The API key of a Bybit account; <c>null</c> on T-Invest.</summary>
    public string? ApiKey { get; }

    /// <summary>The uid of a Bybit account; <c>null</c> on T-Invest.</summary>
    public long? Uid { get; }

    /// <summary>The number of the brokerage account on T-Invest; <c>null</c> on Bybit.</summary>
    public string? AccountId { get; }

    public IReadOnlySet<MarketCategory> Categories { get; }

    public DateTimeOffset CreatedAt { get; }

    /// <exception cref="ArgumentException">A Bybit account without a key or a uid, or a T-Invest account without an account number.</exception>
    public ExchangeAccount(
        Guid id,
        string name,
        Exchange exchange,
        string? apiKey,
        long? uid,
        string? accountId,
        IReadOnlySet<MarketCategory> categories,
        DateTimeOffset createdAt)
    {
        if (exchange == Exchange.Bybit && (string.IsNullOrWhiteSpace(apiKey) || uid is null))
        {
            throw new ArgumentException("A Bybit account has an API key and a uid.", nameof(apiKey));
        }

        if (exchange == Exchange.TInvest && string.IsNullOrWhiteSpace(accountId))
        {
            throw new ArgumentException("A T-Invest account has the number of a brokerage account.", nameof(accountId));
        }

        Id = id;
        Name = name;
        Exchange = exchange;
        ApiKey = apiKey;
        Uid = uid;
        AccountId = accountId;
        Categories = categories;
        CreatedAt = createdAt;
    }

    /// <summary>An account of Bybit.</summary>
    public ExchangeAccount(Guid id, string name, string apiKey, long uid, IReadOnlySet<MarketCategory> categories, DateTimeOffset createdAt)
        : this(id, name, Exchange.Bybit, apiKey, uid, null, categories, createdAt)
    {
    }
}