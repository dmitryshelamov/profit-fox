using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.ExchangeAccounts.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.ExchangeAccounts.Converters;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.Persistence;

public class ExchangeAccountDbConverterTests
{
    [Test]
    public void Known_codes_are_read()
    {
        ExchangeAccount account = ExchangeAccountDbConverter.ToDomain(Row("spot", "linear"));

        account.Categories.ShouldBe([MarketCategory.Spot, MarketCategory.Linear], ignoreOrder: true);
    }

    [Test]
    public void Unknown_category_is_not_read()
    {
        Should.Throw<InvalidOperationException>(() => ExchangeAccountDbConverter.ToDomain(Row("inverse")));
    }

    [Test]
    public void Unknown_exchange_is_not_read()
    {
        Should.Throw<InvalidOperationException>(() => ExchangeAccountDbConverter.ToDomain(Row("spot") with { Exchange = "binance" }));
    }

    [Test]
    public void TInvest_account_is_read_with_its_account_number()
    {
        ExchangeAccount account = ExchangeAccountDbConverter.ToDomain(new ExchangeAccountDb
        {
            Id = Guid.NewGuid(),
            Name = "Брокерский",
            Exchange = "tinvest",
            AccountId = "2000000001",
            Categories = ["moex"],
            CreatedAt = DateTimeOffset.Parse("2024-03-10T12:00:00Z")
        });

        (account.Exchange, account.AccountId, account.ApiKey, account.Uid).ShouldBe((Exchange.TInvest, "2000000001", null, null));
        ExchangeAccountDbConverter.FromDomain(account).Exchange.ShouldBe("tinvest");
    }

    private static ExchangeAccountDb Row(params string[] categories)
    {
        return new ExchangeAccountDb
        {
            Id = Guid.NewGuid(),
            Name = "Main",
            Exchange = "bybit",
            ApiKey = "key",
            Uid = 100,
            Categories = categories,
            CreatedAt = DateTimeOffset.Parse("2024-03-10T12:00:00Z")
        };
    }
}