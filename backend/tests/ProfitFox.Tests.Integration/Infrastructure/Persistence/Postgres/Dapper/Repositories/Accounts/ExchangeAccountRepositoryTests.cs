using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.Accounts;
using ProfitFox.Domain.Repositories;
using ProfitFox.Infrastructure.Security;
using ProfitFox.Tests.Integration.Presentation;

namespace ProfitFox.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.Accounts;

/// <summary>
///     Change add-exchange-accounts, task 3.2 (design.md, decision 3): the accounts in <c>exchange_accounts</c> — spec
///     exchange-accounts «Секрет в базе», «Имя занято», «Второй ключ того же аккаунта», «Аккаунт удалён», «Тот же UID после
///     удаления».
/// </summary>
[NonParallelizable]
public sealed class ExchangeAccountRepositoryTests
{
    private const string Secret = "s3cr3t-value";
    private static readonly DateTimeOffset Added = new DateTimeOffset(2026, 11, 28, 10, 15, 0, TimeSpan.Zero);
    private readonly AesGcmSecretProtector _protector = new AesGcmSecretProtector(Convert.FromBase64String(BenchTestApplication.TestMasterKey));
    private IExchangeAccountRepository _accounts = null!;

    [SetUp]
    public async Task Empty()
    {
        await BenchDatabase.Reset();
        _accounts = BenchServices.Get<IExchangeAccountRepository>();
    }

    [Test]
    public async Task An_added_account_is_listed_found_and_gives_back_its_secret()
    {
        ExchangeAccount main = Account("Основной", 104857321);
        ExchangeAccount ideas = Account("Суб-счёт идей", 104850011, Added.AddMinutes(1));

        Assert.That(await Add(main), Is.True);
        Assert.That(await Add(ideas), Is.True);

        Assert.That(await _accounts.AllAsync(CancellationToken.None), Is.EqualTo(new[] { main, ideas }));
        Assert.That(await _accounts.FindAsync(main.Id, CancellationToken.None), Is.EqualTo(main));
        Assert.That(_protector.Reveal(main.Id, (await _accounts.SecretAsync(main.Id, CancellationToken.None))!), Is.EqualTo(Secret));
        Assert.That(await _accounts.FindAsync(Guid.NewGuid(), CancellationToken.None), Is.Null);
        Assert.That(await _accounts.SecretAsync(Guid.NewGuid(), CancellationToken.None), Is.Null);
    }

    // Spec «Секрет в базе».
    [Test]
    public async Task No_column_of_the_database_has_the_secret()
    {
        await Add(Account("Основной", 104857321));

        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        string row = await connection.ExecuteScalarAsync<string>("SELECT row_to_json(a)::text || encode(secret_ciphertext, 'escape') || encode(secret_nonce, 'escape') || encode(secret_tag, 'escape') FROM exchange_accounts a") ?? "";
        Assert.That(row, Does.Contain("Основной"));
        Assert.That(row, Does.Not.Contain(Secret));
    }

    // Spec «Имя занято»: without regard to case.
    [Test]
    public async Task A_name_another_account_has_in_any_case_is_refused()
    {
        await Add(Account("Основной", 104857321));

        Assert.That(await Add(Account("основной", 104850011)), Is.False);
        Assert.That(await _accounts.AllAsync(CancellationToken.None), Has.Count.EqualTo(1));
    }

    // Spec «Второй ключ того же аккаунта».
    [Test]
    public async Task A_second_account_of_one_uid_is_refused()
    {
        await Add(Account("Основной", 104857321));

        Assert.That(await Add(Account("Второй ключ", 104857321)), Is.False);
        Assert.That(await _accounts.AllAsync(CancellationToken.None), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task A_rename_keeps_the_key_and_the_uid_and_a_taken_name_is_refused()
    {
        ExchangeAccount old = Account("Старый", 104857321);
        ExchangeAccount other = Account("Основной", 104850011);
        await Add(old);
        await Add(other);

        Assert.That(await _accounts.RenameAsync(old.Id, "Запасной", CancellationToken.None), Is.True);
        Assert.That(await _accounts.RenameAsync(old.Id, "ОСНОВНОЙ", CancellationToken.None), Is.False);
        Assert.That(await _accounts.RenameAsync(old.Id, "запасной", CancellationToken.None), Is.True, "its own name in another case");

        Assert.That(await _accounts.FindAsync(old.Id, CancellationToken.None), Is.EqualTo(old with { Name = "запасной" }));
    }

    // Spec «Аккаунт удалён», «Тот же UID после удаления».
    [Test]
    public async Task A_deleted_account_goes_with_its_secret_and_its_uid_may_be_added_again()
    {
        ExchangeAccount old = Account("Старый", 104850011);
        await Add(old);

        Assert.That(await _accounts.DeleteAsync(old.Id, CancellationToken.None), Is.True);
        Assert.That(await _accounts.DeleteAsync(old.Id, CancellationToken.None), Is.False);

        Assert.That(await _accounts.AllAsync(CancellationToken.None), Is.Empty);
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        Assert.That(await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM exchange_accounts"), Is.Zero);
        Assert.That(await Add(Account("Старый", 104850011)), Is.True);
    }

    // Change add-exchange-accounts, task 3.3 (design.md, decision 11): the margin mode is kept with the account, replaced by
    // the one read later, and may be none.
    [Test]
    public async Task The_margin_mode_is_kept_replaced_and_may_be_none()
    {
        ExchangeAccount main = Account("Основной", 104857321) with { MarginMode = "ISOLATED_MARGIN" };
        ExchangeAccount old = Account("Старый", 104850011);
        await Add(main);
        await Add(old);

        Assert.That((await _accounts.FindAsync(main.Id, CancellationToken.None))!.MarginMode, Is.EqualTo("ISOLATED_MARGIN"));
        Assert.That((await _accounts.FindAsync(old.Id, CancellationToken.None))!.MarginMode, Is.Null);

        Assert.That(await _accounts.SetMarginModeAsync(main.Id, "REGULAR_MARGIN", CancellationToken.None), Is.True);
        Assert.That(await _accounts.SetMarginModeAsync(old.Id, "FUTURE_MARGIN", CancellationToken.None), Is.True);
        Assert.That(await _accounts.SetMarginModeAsync(Guid.NewGuid(), "REGULAR_MARGIN", CancellationToken.None), Is.False);

        Assert.That(
            (await _accounts.AllAsync(CancellationToken.None)).Select(account => (account.Name, account.MarginMode)),
            Is.EquivalentTo(new[] { ("Основной", (string?)"REGULAR_MARGIN"), ("Старый", "FUTURE_MARGIN") }));
        Assert.That(await _accounts.SetMarginModeAsync(main.Id, null, CancellationToken.None), Is.True);
        Assert.That((await _accounts.FindAsync(main.Id, CancellationToken.None))!.MarginMode, Is.Null);
    }

    private Task<bool> Add(ExchangeAccount account)
    {
        return _accounts.AddAsync(account, _protector.Protect(account.Id, Secret), CancellationToken.None);
    }

    private static ExchangeAccount Account(string name, long uid, DateTimeOffset? added = null)
    {
        return new ExchangeAccount(Guid.NewGuid(), name, uid, $"key-{uid}", added ?? Added);
    }
}
