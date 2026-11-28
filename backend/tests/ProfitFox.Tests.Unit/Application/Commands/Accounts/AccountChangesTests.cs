using ProfitFox.Application.Commands.Accounts.DeleteAccount;
using ProfitFox.Application.Commands.Accounts.RenameAccount;
using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.Accounts;
using ProfitFox.Domain.ValueObjects.Accounts;

namespace ProfitFox.Tests.Unit.Application.Commands.Accounts;

/// <summary>
///     Change add-exchange-accounts, task 5.2: spec exchange-accounts «Новое имя», «Имя другого аккаунта», «Аккаунт удалён».
/// </summary>
public sealed class AccountChangesTests
{
    private static readonly DateTimeOffset Added = new DateTimeOffset(2026, 11, 28, 10, 15, 0, TimeSpan.Zero);
    private readonly ExchangeAccount _old = new ExchangeAccount(Guid.NewGuid(), "Старый", 104850011, "key-old", Added);
    private readonly ExchangeAccount _main = new ExchangeAccount(Guid.NewGuid(), "Основной", 104857321, "key-main", Added);
    private MemoryExchangeAccountRepository _accounts = null!;

    [SetUp]
    public async Task Accounts()
    {
        _accounts = new MemoryExchangeAccountRepository();
        var protector = new FakeSecretProtector();
        await _accounts.AddAsync(_old, protector.Protect(_old.Id, "secret-old"), CancellationToken.None);
        await _accounts.AddAsync(_main, protector.Protect(_main.Id, "secret-main"), CancellationToken.None);
    }

    // Spec «Новое имя».
    [Test]
    public async Task A_new_name_keeps_the_uid_and_the_key()
    {
        ExchangeAccount renamed = await Rename(_old.Id, " Запасной ");

        Assert.That(renamed, Is.EqualTo(_old with { Name = "Запасной" }));
        Assert.That(await _accounts.FindAsync(_old.Id, CancellationToken.None), Is.EqualTo(renamed));
    }

    // Spec «Имя другого аккаунта».
    [Test]
    public async Task The_name_of_another_account_is_refused_at_the_name_and_the_name_stays()
    {
        AccountRequestException? refused = Assert.ThrowsAsync<AccountRequestException>(() => Rename(_old.Id, "основной"));

        Assert.That(refused!.Problems, Is.EqualTo(new[] { new AccountProblem("TakenName", "аккаунт «основной» уже есть", "name") }));
        Assert.That((await _accounts.FindAsync(_old.Id, CancellationToken.None))!.Name, Is.EqualTo("Старый"));
    }

    [Test]
    public void An_empty_name_is_refused_and_an_account_that_is_not_there_is_not_found()
    {
        Assert.That(Assert.ThrowsAsync<AccountRequestException>(() => Rename(_old.Id, "  "))!.Problems.Single().Field, Is.EqualTo("name"));
        Assert.ThrowsAsync<KeyNotFoundException>(() => Rename(Guid.NewGuid(), "Запасной"));
    }

    // Spec «Аккаунт удалён».
    [Test]
    public async Task A_deleted_account_is_gone_with_its_secret()
    {
        var delete = new DeleteAccountCommandHandler(_accounts);

        Assert.That(await delete.Handle(new DeleteAccountCommand(_old.Id), CancellationToken.None), Is.True);
        Assert.That(await delete.Handle(new DeleteAccountCommand(_old.Id), CancellationToken.None), Is.False);

        Assert.That(await _accounts.AllAsync(CancellationToken.None), Is.EqualTo(new[] { _main }));
        Assert.That(await _accounts.SecretAsync(_old.Id, CancellationToken.None), Is.Null);
    }

    private Task<ExchangeAccount> Rename(Guid id, string name)
    {
        return new RenameAccountCommandHandler(_accounts).Handle(new RenameAccountCommand(id, name), CancellationToken.None);
    }
}
