using NSubstitute;

using ProfitFox.Application.LiveTrading;
using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.ExchangeAccounts;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.LiveTrading;

/// <summary>Decision 2: the key of an account is built by its exchange.</summary>
public class LiveRequestsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:00:00Z");

    [Test]
    public async Task Key_of_a_bybit_account_is_its_api_key_and_secret()
    {
        var account = new ExchangeAccount(Guid.NewGuid(), "Основной", Exchange.Bybit, "api-key", 100, null, new HashSet<MarketCategory> { MarketCategory.Spot }, Now);

        ExchangeKey key = await KeyAsync(account, "api-secret");

        key.ShouldBe(new ExchangeKey("api-key", "api-secret", Exchange.Bybit));
    }

    // On T-Invest the key is the number of the brokerage account with the token.
    [Test]
    public async Task Key_of_a_tinvest_account_is_its_account_number_and_token()
    {
        var account = new ExchangeAccount(Guid.NewGuid(), "Брокерский", Exchange.TInvest, null, null, "2000000001", new HashSet<MarketCategory> { MarketCategory.Moex }, Now);

        ExchangeKey key = await KeyAsync(account, "t.token");

        key.ShouldBe(new ExchangeKey("2000000001", "t.token", Exchange.TInvest));
    }

    private static Task<ExchangeKey> KeyAsync(ExchangeAccount account, string secret)
    {
        var encrypted = new EncryptedSecret([1], [2], [3]);
        IExchangeAccountRepository accounts = Substitute.For<IExchangeAccountRepository>();
        accounts.GetSecretAsync(account.Id, Arg.Any<CancellationToken>()).Returns(encrypted);
        ISecretProtector protector = Substitute.For<ISecretProtector>();
        protector.Decrypt(account.Id, encrypted).Returns(secret);

        return LiveRequests.KeyAsync(accounts, protector, account, CancellationToken.None);
    }
}