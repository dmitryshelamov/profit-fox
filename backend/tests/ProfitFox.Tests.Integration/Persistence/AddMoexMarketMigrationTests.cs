using Dapper;

using FluentMigrator.Runner;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.Infrastructure.Persistence.Postgres;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>Decisions 2, 4 and 12: accounts get an exchange, a T-Invest account is stored once, and a series has a border of coverage.</summary>
public class AddMoexMarketMigrationTests
{
    // The last migration before the exchange of an account.
    private const long BeforeMoex = 202611080007;

    private static readonly DateTimeOffset Created = DateTimeOffset.Parse("2024-03-10T12:00:00Z");

    private ServiceProvider _migrations = null!;
    private NpgsqlDataSource _dataSource = null!;

    [SetUp]
    public async Task SetUp()
    {
        string connectionString = await IntegrationEnvironment.CreateEmptyDatabaseAsync();
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:MarketData"] = connectionString })
            .Build();
        var services = new ServiceCollection();
        PostgresRegistrar.Configure(configuration, services);
        _migrations = services.BuildServiceProvider();
        Migrate(BeforeMoex);
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
        await _migrations.DisposeAsync();
    }

    [Test]
    public async Task Old_accounts_become_bybit_accounts()
    {
        var main = Guid.NewGuid();
        await using (NpgsqlConnection connection = await _dataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO exchange_accounts (id, name, api_key, secret_nonce, secret_ciphertext, secret_tag, uid, categories, created_at)
                VALUES (@Main, 'Main', 'key-1', '\x00', '\x00', '\x00', 100, ARRAY['spot'], @Created);
                """,
                new { Main = main, Created });
        }

        Migrate(null);

        await using NpgsqlConnection read = await _dataSource.OpenConnectionAsync();
        (await read.QuerySingleAsync<string>("SELECT exchange FROM exchange_accounts WHERE id = @Main", new { Main = main })).ShouldBe("bybit");
    }

    // Scenario "Второй токен того же счёта": the database itself refuses the second account of a brokerage account.
    [Test]
    public async Task Second_account_of_the_same_brokerage_account_is_refused()
    {
        Migrate(null);

        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(InsertTInvest, new { Id = Guid.NewGuid(), Created });

        PostgresException exception = await Should.ThrowAsync<PostgresException>(() => connection.ExecuteAsync(InsertTInvest, new { Id = Guid.NewGuid(), Created }));
        exception.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    [Test]
    public async Task Uid_of_a_bybit_account_is_still_stored_once()
    {
        Migrate(null);

        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(InsertBybit, new { Id = Guid.NewGuid(), Created });

        PostgresException exception = await Should.ThrowAsync<PostgresException>(() => connection.ExecuteAsync(InsertBybit, new { Id = Guid.NewGuid(), Created }));
        exception.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    [Test]
    public async Task Coverage_of_a_series_is_stored_and_read()
    {
        Migrate(null);

        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO candle_coverage (category, symbol, "interval", covered_from, covered_until)
            VALUES ('moex', 'SBER', '60', @From, @Until)
            """,
            new { From = Created, Until = Created.AddDays(1) });

        (await connection.QuerySingleAsync<DateTime>("""SELECT covered_until FROM candle_coverage WHERE category = 'moex' AND symbol = 'SBER' AND "interval" = '60'"""))
            .ShouldBe(Created.AddDays(1).UtcDateTime);
    }

    private const string InsertTInvest = """
                                         INSERT INTO exchange_accounts (id, name, exchange, account_id, secret_nonce, secret_ciphertext, secret_tag, categories, created_at)
                                         VALUES (@Id, 'Брокерский', 'tinvest', '2000000001', '\x00', '\x00', '\x00', ARRAY['moex'], @Created)
                                         """;

    private const string InsertBybit = """
                                       INSERT INTO exchange_accounts (id, name, exchange, api_key, uid, secret_nonce, secret_ciphertext, secret_tag, categories, created_at)
                                       VALUES (@Id, 'Main', 'bybit', 'key', 100, '\x00', '\x00', '\x00', ARRAY['spot'], @Created)
                                       """;

    /// <summary>Migrates up to <paramref name="version" />, or to the latest one.</summary>
    private void Migrate(long? version)
    {
        using IServiceScope scope = _migrations.CreateScope();
        IMigrationRunner runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();

        if (version is { } target)
        {
            runner.MigrateUp(target);
        }
        else
        {
            runner.MigrateUp();
        }
    }
}