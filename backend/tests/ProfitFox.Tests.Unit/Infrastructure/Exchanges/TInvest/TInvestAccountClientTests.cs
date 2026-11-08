using Grpc.Core;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Infrastructure.Exchanges.TInvest;

using Shouldly;

using Tinkoff.InvestApi.V1;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.TInvest;

/// <summary>Requirements "Счета токена T-Invest" and "Баланс аккаунта T-Invest" at the adapter.</summary>
public class TInvestAccountClientTests
{
    private TInvestTestHost _host = null!;

    [SetUp]
    public void SetUp()
    {
        _host = new TInvestTestHost();
    }

    [TearDown]
    public void TearDown()
    {
        _host.Dispose();
    }

    [Test]
    public async Task Accounts_come_with_their_type_status_and_access()
    {
        _host.Api.On<GetAccountsRequest, GetAccountsResponse>(
            "GetAccounts",
            _ => new GetAccountsResponse
            {
                Accounts =
                {
                    new Account { Id = "2000000001", Name = "Брокерский счёт", Type = AccountType.Tinkoff, Status = AccountStatus.Open, AccessLevel = AccessLevel.AccountAccessLevelFullAccess },
                    new Account { Id = "2000000002", Name = "ИИС", Type = AccountType.TinkoffIis, Status = AccountStatus.Closed, AccessLevel = AccessLevel.AccountAccessLevelReadOnly },
                    new Account { Id = "2000000003", Name = "Инвесткопилка", Type = AccountType.InvestBox, Status = AccountStatus.New, AccessLevel = AccessLevel.AccountAccessLevelNoAccess }
                }
            });

        IReadOnlyList<BrokerAccount> accounts = await _host.AccountClient.ListAccountsAsync("t.user", CancellationToken.None);

        accounts.ShouldBe(
        [
            new BrokerAccount("2000000001", "Брокерский счёт", BrokerAccountType.Brokerage, BrokerAccountStatus.Open, BrokerAccountAccess.Full),
            new BrokerAccount("2000000002", "ИИС", BrokerAccountType.Iis, BrokerAccountStatus.Closed, BrokerAccountAccess.ReadOnly),
            new BrokerAccount("2000000003", "Инвесткопилка", BrokerAccountType.Other, BrokerAccountStatus.New, BrokerAccountAccess.None)
        ]);
        _host.Api.Calls.Single().Header("authorization").ShouldBe("Bearer t.user");
    }

    [Test]
    public async Task Rejected_token_is_a_rejected_key()
    {
        _host.Api.Fail("GetAccounts", StatusCode.Unauthenticated, "40003");

        await Should.ThrowAsync<ExchangeKeyRejectedException>(() => _host.AccountClient.ListAccountsAsync("t.wrong", CancellationToken.None));
    }

    // Scenario "Портфель со стоимостью".
    [Test]
    public async Task Portfolio_is_the_roubles_the_shares_and_the_total_in_roubles()
    {
        _host.Api.On<PortfolioRequest, PortfolioResponse>(
            "GetPortfolio",
            _ => new PortfolioResponse
            {
                TotalAmountPortfolio = Money(53000m),
                TotalAmountCurrencies = Money(50000m),
                Positions =
                {
                    new PortfolioPosition { InstrumentType = "currency", Ticker = "RUB000UTSTOM", Quantity = 50000m.ToQuotation(), CurrentPrice = Money(1m) },
                    new PortfolioPosition { InstrumentType = "share", Ticker = "SBER", Quantity = 10m.ToQuotation(), CurrentPrice = Money(300m) },
                    new PortfolioPosition { InstrumentType = "share", Ticker = "GAZP", Quantity = 0m.ToQuotation(), CurrentPrice = Money(150m) }
                }
            });

        AccountBalance balance = await _host.AccountClient.GetPortfolioAsync("t.user", "2000000001", CancellationToken.None);

        (balance.Currency, balance.TotalEquity).ShouldBe(("RUB", 53000m));
        balance.Coins.ShouldBe([new CoinBalance("RUB", 50000m, 50000m), new CoinBalance("SBER", 10m, 3000m)]);
        var request = (PortfolioRequest)_host.Api.Calls.Single().Request;
        (request.AccountId, request.Currency).ShouldBe(("2000000001", PortfolioRequest.Types.CurrencyRequest.Rub));
    }

    private static MoneyValue Money(decimal value)
    {
        var quotation = value.ToQuotation();

        return new MoneyValue { Currency = "rub", Units = quotation.Units, Nano = quotation.Nano };
    }
}