using ProfitFox.Application.ExchangeAccounts;
using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.ExchangeAccounts;

namespace ProfitFox.Presentation.Contracts.ExchangeAccounts;

/// <summary>
///     An account without its secret: the API never returns it. <c>exchange</c> is <c>bybit</c> or <c>tinvest</c>; a Bybit account has
///     <c>apiKey</c> and <c>uid</c>, a T-Invest account <c>accountId</c>, the number of its brokerage account.
/// </summary>
public sealed record ExchangeAccountResponse(
    Guid Id,
    string Name,
    string Exchange,
    string? ApiKey,
    long? Uid,
    string? AccountId,
    IReadOnlyList<string> Categories,
    DateTimeOffset CreatedAt)
{
    public static ExchangeAccountResponse Of(ExchangeAccount account)
    {
        return new ExchangeAccountResponse(
            account.Id,
            account.Name,
            account.Exchange.ToCode(),
            account.ApiKey,
            account.Uid,
            account.AccountId,
            [.. account.Categories.Order().Select(category => category.ToCode())],
            account.CreatedAt);
    }
}