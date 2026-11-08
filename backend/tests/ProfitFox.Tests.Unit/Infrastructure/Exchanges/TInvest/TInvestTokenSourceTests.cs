using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.ExchangeAccounts;
using ProfitFox.Infrastructure.Exchanges.TInvest;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.TInvest;

/// <summary>Decision 3 and requirement "Аккаунт T-Invest как источник данных MOEX".</summary>
public class TInvestTokenSourceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:00:00Z");
    private static readonly EncryptedSecret Encrypted = new EncryptedSecret([1], [2], [3]);
    private static readonly EncryptedSecret EncryptedOther = new EncryptedSecret([4], [5], [6]);

    private IExchangeAccountRepository _accounts = null!;
    private ISecretProtector _protector = null!;
    private FakeTimeProvider _time = null!;
    private TInvestTokenSource _source = null!;

    [SetUp]
    public void SetUp()
    {
        _accounts = Substitute.For<IExchangeAccountRepository>();
        _accounts.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        _protector = Substitute.For<ISecretProtector>();
        _time = new FakeTimeProvider(Now);
        ServiceProvider provider = new ServiceCollection().AddScoped(_ => _accounts).BuildServiceProvider();
        _source = new TInvestTokenSource(provider.GetRequiredService<IServiceScopeFactory>(), _protector, _time);
    }

    // Scenario "Нет аккаунта T-Invest".
    [Test]
    public async Task Without_a_tinvest_account_the_data_are_unavailable()
    {
        var bybit = new ExchangeAccount(Guid.NewGuid(), "Main", "key", 100, new HashSet<MarketCategory> { MarketCategory.Spot }, Now);
        _accounts.GetAllAsync(Arg.Any<CancellationToken>()).Returns([bybit]);

        ExchangeUnavailableException exception = await Should.ThrowAsync<ExchangeUnavailableException>(() => _source.GetAsync(CancellationToken.None));

        exception.Message.ShouldBe("T-Invest data is unavailable: there is no T-Invest account.");
        await _accounts.DidNotReceiveWithAnyArgs().GetSecretAsync(default, default);
    }

    // Scenario "Аккаунт появился".
    [Test]
    public async Task Token_of_the_oldest_tinvest_account_is_decrypted()
    {
        ExchangeAccount older = TInvest("2000000001", Now.AddDays(-2));
        ExchangeAccount newer = TInvest("2000000002", Now.AddDays(-1));
        _accounts.GetAllAsync(Arg.Any<CancellationToken>()).Returns([newer, older]);
        _accounts.GetSecretAsync(older.Id, Arg.Any<CancellationToken>()).Returns(Encrypted);
        _accounts.GetSecretAsync(newer.Id, Arg.Any<CancellationToken>()).Returns(EncryptedOther);
        _protector.Decrypt(older.Id, Encrypted).Returns("t.older");
        _protector.Decrypt(newer.Id, EncryptedOther).Returns("t.newer");

        (await _source.GetAsync(CancellationToken.None)).ShouldBe("t.older");
    }

    [Test]
    public async Task Second_read_within_five_minutes_does_not_ask_storage()
    {
        ExchangeAccount account = TInvest("2000000001", Now.AddDays(-1));
        _accounts.GetAllAsync(Arg.Any<CancellationToken>()).Returns([account]);
        _accounts.GetSecretAsync(account.Id, Arg.Any<CancellationToken>()).Returns(Encrypted);
        _protector.Decrypt(account.Id, Encrypted).Returns("t.token");

        await _source.GetAsync(CancellationToken.None);
        _time.Advance(TInvestTokenSource.Lifetime - TimeSpan.FromTicks(1));
        (await _source.GetAsync(CancellationToken.None)).ShouldBe("t.token");

        await _accounts.Received(1).GetAllAsync(Arg.Any<CancellationToken>());
    }

    // Requirement: a deleted account stops being used within five minutes.
    [Test]
    public async Task Five_minutes_after_the_account_is_deleted_the_source_is_empty()
    {
        ExchangeAccount account = TInvest("2000000001", Now.AddDays(-1));
        _accounts.GetAllAsync(Arg.Any<CancellationToken>()).Returns([account]);
        _accounts.GetSecretAsync(account.Id, Arg.Any<CancellationToken>()).Returns(Encrypted);
        _protector.Decrypt(account.Id, Encrypted).Returns("t.token");
        await _source.GetAsync(CancellationToken.None);
        _accounts.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);

        _time.Advance(TInvestTokenSource.Lifetime);

        await Should.ThrowAsync<ExchangeUnavailableException>(() => _source.GetAsync(CancellationToken.None));
    }

    // An account added right after a failed read is seen at once: a miss is not kept.
    [Test]
    public async Task Account_added_after_a_miss_is_used_at_once()
    {
        await Should.ThrowAsync<ExchangeUnavailableException>(() => _source.GetAsync(CancellationToken.None));
        ExchangeAccount account = TInvest("2000000001", Now);
        _accounts.GetAllAsync(Arg.Any<CancellationToken>()).Returns([account]);
        _accounts.GetSecretAsync(account.Id, Arg.Any<CancellationToken>()).Returns(Encrypted);
        _protector.Decrypt(account.Id, Encrypted).Returns("t.token");

        (await _source.GetAsync(CancellationToken.None)).ShouldBe("t.token");
    }

    private static ExchangeAccount TInvest(string accountId, DateTimeOffset createdAt)
    {
        return new ExchangeAccount(Guid.NewGuid(), "Брокерский", Exchange.TInvest, null, null, accountId, new HashSet<MarketCategory> { MarketCategory.Moex }, createdAt);
    }
}