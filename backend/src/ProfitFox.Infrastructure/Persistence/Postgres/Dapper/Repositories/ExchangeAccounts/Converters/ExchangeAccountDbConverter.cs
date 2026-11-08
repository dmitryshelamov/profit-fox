using ProfitFox.Application.ExchangeAccounts;
using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.ExchangeAccounts.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.ExchangeAccounts.Converters;

internal static class ExchangeAccountDbConverter
{
    internal static ExchangeAccountDb FromDomain(ExchangeAccount account)
    {
        return new ExchangeAccountDb
        {
            Id = account.Id,
            Name = account.Name,
            Exchange = account.Exchange.ToCode(),
            ApiKey = account.ApiKey,
            Uid = account.Uid,
            AccountId = account.AccountId,
            Categories = [.. account.Categories.Order().Select(category => category.ToCode())],
            CreatedAt = account.CreatedAt
        };
    }

    internal static ExchangeAccount ToDomain(ExchangeAccountDb account)
    {
        return new ExchangeAccount(
            account.Id,
            account.Name,
            Exchange(account.Id, account.Exchange),
            account.ApiKey,
            account.Uid,
            account.AccountId,
            account.Categories.Select(code => Category(account.Id, code)).ToHashSet(),
            account.CreatedAt);
    }

    private static Exchange Exchange(Guid id, string code)
    {
        return ExchangeCodes.TryParse(code, out Exchange exchange)
            ? exchange
            : throw new InvalidOperationException($"Exchange account {id} has an unknown exchange '{code}'.");
    }

    private static MarketCategory Category(Guid id, string code)
    {
        return MarketDataCodes.TryParseCategory(code, out MarketCategory category)
            ? category
            : throw new InvalidOperationException($"Exchange account {id} has an unknown market category '{code}'.");
    }
}