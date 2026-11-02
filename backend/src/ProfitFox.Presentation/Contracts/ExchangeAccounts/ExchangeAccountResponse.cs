using ProfitFox.Application.ExchangeAccounts;
using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.ExchangeAccounts;

namespace ProfitFox.Presentation.Contracts.ExchangeAccounts;

/// <summary>An account without its secret: the API never returns it.</summary>
public sealed record ExchangeAccountResponse(Guid Id, string Name, string Environment, string ApiKey, long Uid, IReadOnlyList<string> Categories, DateTimeOffset CreatedAt)
{
    public static ExchangeAccountResponse Of(ExchangeAccount account)
    {
        return new ExchangeAccountResponse(
            account.Id,
            account.Name,
            account.Environment.ToCode(),
            account.ApiKey,
            account.Uid,
            [.. account.Categories.Order().Select(category => category.ToCode())],
            account.CreatedAt);
    }
}