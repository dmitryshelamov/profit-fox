using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Queries.Accounts;
using ProfitFox.Domain.Entities.Accounts;
using ProfitFox.Tests.Shared.Fakes.Exchange;
using ProfitFox.Tests.Unit.Application.Commands.Accounts;

namespace ProfitFox.Tests.Unit.Application.Queries.Accounts;

/// <summary>
///     Change add-exchange-accounts, task 5.2: the list and the free money — spec exchange-accounts «Два аккаунта», «Баланс
///     показан», «Ключ больше не действует», «Сменили мастер-ключ».
/// </summary>
public sealed class GetAccountBalanceQueryHandlerTests
{
    private static readonly DateTimeOffset Added = new DateTimeOffset(2026, 11, 28, 10, 15, 0, TimeSpan.Zero);
    private readonly ExchangeAccount _main = new ExchangeAccount(Guid.NewGuid(), "Основной", 104857321, "key-main", Added);
    private readonly ExchangeAccount _ideas = new ExchangeAccount(Guid.NewGuid(), "Суб-счёт идей", 104850011, "key-ideas", Added.AddMinutes(1));
    private MemoryExchangeAccountRepository _accounts = null!;
    private FakeKeyInspector _bybit = null!;
    private FakeSecretProtector _protector = null!;

    [SetUp]
    public async Task Accounts()
    {
        _accounts = new MemoryExchangeAccountRepository();
        _bybit = new FakeKeyInspector();
        _protector = new FakeSecretProtector();
        await _accounts.AddAsync(_main, _protector.Protect(_main.Id, "secret-main"), CancellationToken.None);
        await _accounts.AddAsync(_ideas, _protector.Protect(_ideas.Id, "secret-ideas"), CancellationToken.None);
    }

    // Spec «Два аккаунта».
    [Test]
    public async Task The_list_gives_both_accounts_in_the_order_they_were_added()
    {
        IReadOnlyList<ExchangeAccount> accounts = await new GetAccountsQueryHandler(_accounts).Handle(new GetAccountsQuery(), CancellationToken.None);

        Assert.That(accounts, Is.EqualTo(new[] { _main, _ideas }));
    }

    // Spec «Баланс показан».
    [Test]
    public async Task The_free_money_is_read_at_Bybit_with_the_decrypted_secret_and_not_written()
    {
        _bybit.Free("key-main", 1250.40m);

        decimal free = await Balance(_main.Id);

        Assert.That(free, Is.EqualTo(1250.40m));
        Assert.That(_bybit.Calls, Is.EqualTo(new[] { ("free", "key-main", "secret-main") }));
        Assert.That(_accounts.Writes, Is.EqualTo(2), "only the two additions");
    }

    // Spec «Ключ больше не действует».
    [Test]
    public async Task A_key_Bybit_no_longer_takes_tells_the_reason_and_the_account_stays()
    {
        _bybit.Free("key-main", new ExchangeKeyRejectedException("Your api key has expired."));

        ExchangeKeyRejectedException? rejected = Assert.ThrowsAsync<ExchangeKeyRejectedException>(() => Balance(_main.Id));

        Assert.That(rejected!.Message, Is.EqualTo("Your api key has expired."));
        Assert.That(await _accounts.FindAsync(_main.Id, CancellationToken.None), Is.EqualTo(_main));
    }

    // Spec «Сменили мастер-ключ».
    [Test]
    public void A_secret_of_another_master_key_does_not_read_and_Bybit_is_not_called()
    {
        _protector.Changed = true;

        Assert.ThrowsAsync<SecretUnreadableException>(() => Balance(_main.Id));
        Assert.That(_bybit.Calls, Is.Empty);
    }

    [Test]
    public void The_balance_of_an_account_that_is_not_there_is_not_found()
    {
        Assert.ThrowsAsync<KeyNotFoundException>(() => Balance(Guid.NewGuid()));
    }

    private Task<decimal> Balance(Guid id)
    {
        return new GetAccountBalanceQueryHandler(_accounts, _bybit, _protector).Handle(new GetAccountBalanceQuery(id), CancellationToken.None);
    }
}
