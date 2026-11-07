using System.Text;

using Dapper;

using Npgsql;

using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.ValueObjects.ExchangeAccounts;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.ExchangeAccounts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

public class ExchangeAccountRepositoryTests
{
    private static readonly DateTimeOffset Created = DateTimeOffset.Parse("2024-03-10T12:00:00Z");
    private static readonly EncryptedSecret Secret = new EncryptedSecret([1, 2, 3], Encoding.UTF8.GetBytes("cipher"), [4, 5, 6]);

    private NpgsqlDataSource _dataSource = null!;
    private ExchangeAccountRepository _repository = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dataSource = MarketDataDatabase.Open();
        _repository = new ExchangeAccountRepository(_dataSource);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
    }

    [Test]
    public async Task Account_is_read_back_with_its_secret_apart()
    {
        ExchangeAccount account = Account(uid: 100);

        (await _repository.AddAsync(account, Secret, CancellationToken.None)).ShouldBeTrue();

        ExchangeAccount? read = await _repository.GetAsync(account.Id, CancellationToken.None);
        read.ShouldNotBeNull();
        read.ShouldBeEquivalentTo(account);
        (await _repository.GetAllAsync(CancellationToken.None)).Single().Id.ShouldBe(account.Id);
        EncryptedSecret? secret = await _repository.GetSecretAsync(account.Id, CancellationToken.None);
        secret.ShouldNotBeNull();
        secret.Nonce.ShouldBe(Secret.Nonce);
        secret.Ciphertext.ShouldBe(Secret.Ciphertext);
        secret.Tag.ShouldBe(Secret.Tag);
    }

    [Test]
    public async Task Unknown_account_is_not_found()
    {
        (await _repository.GetAsync(Guid.NewGuid(), CancellationToken.None)).ShouldBeNull();
        (await _repository.GetSecretAsync(Guid.NewGuid(), CancellationToken.None)).ShouldBeNull();
        (await _repository.RenameAsync(Guid.NewGuid(), "Other", CancellationToken.None)).ShouldBeFalse();
        (await _repository.DeleteAsync(Guid.NewGuid(), CancellationToken.None)).ShouldBeFalse();
    }

    // Scenario "Повторный ключ того же аккаунта".
    [Test]
    public async Task One_uid_is_stored_once()
    {
        await _repository.AddAsync(Account(uid: 100), Secret, CancellationToken.None);

        (await _repository.AddAsync(Account(uid: 100), Secret, CancellationToken.None)).ShouldBeFalse();
        (await _repository.GetAllAsync(CancellationToken.None)).Count.ShouldBe(1);
    }

    // Scenario "Secret в базе зашифрован".
    [Test]
    public async Task The_row_holds_only_the_encrypted_secret()
    {
        ExchangeAccount account = Account(uid: 100);
        await _repository.AddAsync(account, Secret, CancellationToken.None);

        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
        string row = await connection.ExecuteScalarAsync<string>("SELECT row_to_json(a)::text FROM exchange_accounts a") ?? string.Empty;

        row.ShouldNotContain("plain-secret");
        row.ShouldContain(account.ApiKey);
    }

    // An unknown category must never be read as one.
    [TestCase("categories", "ARRAY['spot', 'inverse']")]
    [TestCase("categories", "ARRAY[]::text[]")]
    public async Task The_table_takes_only_known_codes(string column, string value)
    {
        await _repository.AddAsync(Account(uid: 100), Secret, CancellationToken.None);

        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();

        PostgresException exception = await Should.ThrowAsync<PostgresException>(() => connection.ExecuteAsync($"UPDATE exchange_accounts SET {column} = {value}"));
        exception.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    [Test]
    public async Task Rename_changes_only_the_name()
    {
        ExchangeAccount account = Account(uid: 100);
        await _repository.AddAsync(account, Secret, CancellationToken.None);

        (await _repository.RenameAsync(account.Id, "Testnet", CancellationToken.None)).ShouldBeTrue();

        (await _repository.GetAsync(account.Id, CancellationToken.None)).ShouldBeEquivalentTo(account with { Name = "Testnet" });
    }

    [Test]
    public async Task Deleted_account_is_gone()
    {
        ExchangeAccount account = Account(uid: 100);
        await _repository.AddAsync(account, Secret, CancellationToken.None);

        (await _repository.DeleteAsync(account.Id, CancellationToken.None)).ShouldBeTrue();

        (await _repository.GetAsync(account.Id, CancellationToken.None)).ShouldBeNull();
    }

    // The foreign key guards a bot saved while its account is being deleted.
    [Test]
    public async Task Account_a_bot_refers_to_is_not_deleted()
    {
        ExchangeAccount account = Account(uid: 100);
        await _repository.AddAsync(account, Secret, CancellationToken.None);
        Guid botId = await AddBotAsync();
        await using (NpgsqlConnection connection = await _dataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync("UPDATE bots SET exchange_account_id = @Account WHERE id = @Bot", new { Account = account.Id, Bot = botId });
        }

        await Should.ThrowAsync<ExchangeAccountInUseException>(() => _repository.DeleteAsync(account.Id, CancellationToken.None));

        (await _repository.GetAsync(account.Id, CancellationToken.None)).ShouldNotBeNull();
    }

    private async Task<Guid> AddBotAsync()
    {
        var library = new StrategyLibrary(0, [], []);
        Strategy ema = library.AddStrategy(Guid.NewGuid(), "EMA", StrategyJson.ParseStrategy(StrategyFixtures.Element("ema")), Created);
        Bot bot = library.AddBot(Guid.NewGuid(), "BTC EMA", ema.Id, new Dictionary<string, NodeValue>(), new Instrument(MarketCategory.Spot, "BTCUSDT"), Interval.OneHour, 1000m, Created);
        await new StrategyLibraryRepository(_dataSource).SaveAsync(library.Change, 0, CancellationToken.None);

        return bot.Id;
    }

    private static ExchangeAccount Account(long uid)
    {
        return new ExchangeAccount(
            Guid.NewGuid(),
            "Main",
            $"key-{Guid.NewGuid():N}",
            uid,
            new HashSet<MarketCategory> { MarketCategory.Spot, MarketCategory.Linear },
            Created);
    }
}