using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.Commands.Accounts.AddAccount;
using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.Accounts;
using ProfitFox.Domain.ValueObjects.Accounts;
using ProfitFox.Tests.Shared.Fakes.Exchange;

namespace ProfitFox.Tests.Unit.Application.Commands.Accounts;

/// <summary>
///     Change add-exchange-accounts, task 5.1 (design.md, decision 5): the order «поля → Bybit → правила ключа → шифрование →
///     запись» — spec exchange-accounts «Аккаунт добавлен», «Пустой секрет», «Bybit не знает ключ», «Bybit недоступен»,
///     «Второй ключ того же аккаунта», «Имя занято».
/// </summary>
public sealed class AddAccountCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 28, 10, 15, 0, TimeSpan.Zero);
    private MemoryExchangeAccountRepository _accounts = null!;
    private FakeKeyInspector _bybit = null!;
    private FakeSecretProtector _protector = null!;

    [SetUp]
    public void Fakes()
    {
        _accounts = new MemoryExchangeAccountRepository();
        _bybit = new FakeKeyInspector();
        _protector = new FakeSecretProtector();
    }

    // Spec «Аккаунт добавлен».
    [Test]
    public async Task A_key_Bybit_takes_is_added_with_its_uid_and_its_secret_encrypted_for_it()
    {
        ExchangeAccount account = await Handle(new AddAccountCommand("  Основной ", " key-1 ", " s3cr3t-value "));

        Assert.That(account, Is.EqualTo(new ExchangeAccount(account.Id, "Основной", FakeKeyInspector.DefaultUid, "key-1", Now)));
        Assert.That(await _accounts.AllAsync(CancellationToken.None), Is.EqualTo(new[] { account }));
        Assert.That(_bybit.Calls, Is.EqualTo(new[] { ("inspect", "key-1", "s3cr3t-value") }));
        Assert.That(_protector.Reveal(account.Id, _accounts.Kept(account.Id)), Is.EqualTo("s3cr3t-value"));
    }

    // Spec «Пустой секрет».
    [Test]
    public void An_account_without_a_secret_is_refused_at_the_secret_without_a_call_of_Bybit()
    {
        AccountRequestException? refused = Assert.ThrowsAsync<AccountRequestException>(() => Handle(new AddAccountCommand("Основной", "key-1", "")));

        Assert.That(refused!.Problems.Select(problem => problem.Field), Is.EqualTo(new[] { "secret" }));
        Assert.That(_bybit.Calls, Is.Empty);
        Assert.That(_accounts.Writes, Is.Zero);
    }

    // Spec «Bybit не знает ключ».
    [Test]
    public void A_key_Bybit_does_not_know_is_rejected_and_not_saved()
    {
        _bybit.Key("key-1", new ExchangeKeyRejectedException("API key is invalid."));

        ExchangeKeyRejectedException? rejected = Assert.ThrowsAsync<ExchangeKeyRejectedException>(() => Handle(new AddAccountCommand("Основной", "key-1", "secret")));

        Assert.That(rejected!.Message, Is.EqualTo("API key is invalid."));
        Assert.That(_accounts.Writes, Is.Zero);
    }

    // Spec «Bybit недоступен».
    [Test]
    public void An_account_is_not_saved_when_Bybit_does_not_answer()
    {
        _bybit.Key("key-1", new ExchangeUnavailableException("Bybit is unavailable: timeout"));

        Assert.ThrowsAsync<ExchangeUnavailableException>(() => Handle(new AddAccountCommand("Основной", "key-1", "secret")));

        Assert.That(_accounts.Writes, Is.Zero);
    }

    // Spec «Ключ с правом вывода» through the handler: the rules of the key after Bybit, before the write.
    [Test]
    public void A_key_the_rules_refuse_is_not_saved()
    {
        _bybit.Key("key-1", FakeKeyInspector.Trading() with { Wallet = ["Withdraw"] });

        AccountRequestException? refused = Assert.ThrowsAsync<AccountRequestException>(() => Handle(new AddAccountCommand("Основной", "key-1", "secret")));

        Assert.That(refused!.Problems, Is.EqualTo(new[] { new AccountProblem("WithdrawPermission", "у ключа есть право вывода", "apiKey") }));
        Assert.That(_accounts.Writes, Is.Zero);
    }

    // Spec «Второй ключ того же аккаунта».
    [Test]
    public async Task A_second_key_of_an_added_account_is_refused_with_the_name_it_was_added_under()
    {
        _bybit.Key("key-1", FakeKeyInspector.Trading(104857321)).Key("key-2", FakeKeyInspector.Trading(104857321));
        await Handle(new AddAccountCommand("Основной", "key-1", "secret"));

        AccountRequestException? refused = Assert.ThrowsAsync<AccountRequestException>(() => Handle(new AddAccountCommand("Второй", "key-2", "secret")));

        Assert.That(refused!.Problems, Is.EqualTo(new[] { new AccountProblem("TakenAccount", "этот аккаунт Bybit уже добавлен под именем «Основной»", "apiKey") }));
        Assert.That(await _accounts.AllAsync(CancellationToken.None), Has.Count.EqualTo(1));
    }

    // Spec «Имя занято».
    [Test]
    public async Task A_name_another_account_has_in_another_case_is_refused_at_the_name()
    {
        _bybit.Key("key-1", FakeKeyInspector.Trading(1)).Key("key-2", FakeKeyInspector.Trading(2));
        await Handle(new AddAccountCommand("Основной", "key-1", "secret"));

        AccountRequestException? refused = Assert.ThrowsAsync<AccountRequestException>(() => Handle(new AddAccountCommand("основной", "key-2", "secret")));

        Assert.That(refused!.Problems, Is.EqualTo(new[] { new AccountProblem("TakenName", "аккаунт «основной» уже есть", "name") }));
    }

    // The database keeps a moment to the microsecond, and a clock of Linux ticks every 100 ns: the account is answered with
    // the moment it is read back with, not with seven digits the table drops.
    [Test]
    public async Task The_moment_of_an_account_is_cut_to_the_microsecond_the_database_keeps()
    {
        DateTimeOffset precise = Now.AddTicks(1_234_567);

        ExchangeAccount account = await Handle(new AddAccountCommand("Основной", "key-1", "secret"), precise);

        Assert.That(account.CreatedAt, Is.EqualTo(Now.AddTicks(1_234_560)));
        Assert.That((await _accounts.AllAsync(CancellationToken.None)).Single().CreatedAt, Is.EqualTo(account.CreatedAt));
    }

    private Task<ExchangeAccount> Handle(AddAccountCommand command, DateTimeOffset? now = null)
    {
        return new AddAccountCommandHandler(_accounts, _bybit, _protector, new FakeTimeProvider(now ?? Now)).Handle(command, CancellationToken.None);
    }
}
