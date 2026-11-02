using Dapper;

using FluentMigrator.Runner;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.Infrastructure.Persistence.Postgres;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>Testnet leaves the product: its accounts go, bots that traded with them stay without an account.</summary>
public class RemoveTestnetMigrationTests
{
    // The last migration with exchange environments.
    private const long WithEnvironments = 202611040001;

    private static readonly DateTimeOffset Created = DateTimeOffset.Parse("2024-03-10T12:00:00Z");

    private ServiceProvider _migrations = null!;
    private NpgsqlDataSource _dataSource = null!;

    [SetUp]
    public async Task SetUp()
    {
        string connectionString = await IntegrationEnvironment.CreateDatabaseAsync();
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:MarketData"] = connectionString })
            .Build();
        var services = new ServiceCollection();
        PostgresRegistrar.Configure(configuration, services);
        _migrations = services.BuildServiceProvider();
        Migrate(WithEnvironments);
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
        await _migrations.DisposeAsync();
    }

    [Test]
    public async Task Testnet_accounts_go_and_their_bots_lose_the_account()
    {
        var mainnet = Guid.NewGuid();
        var testnet = Guid.NewGuid();
        var strategy = Guid.NewGuid();
        var onTestnet = Guid.NewGuid();
        var onMainnet = Guid.NewGuid();
        await using (NpgsqlConnection connection = await _dataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO exchange_accounts (id, name, environment, api_key, secret_nonce, secret_ciphertext, secret_tag, uid, categories, created_at)
                VALUES (@Mainnet, 'Main', 'mainnet', 'key-1', '\x00', '\x00', '\x00', 100, ARRAY['spot'], @Created),
                       (@Testnet, 'Test', 'testnet', 'key-2', '\x00', '\x00', '\x00', 100, ARRAY['spot'], @Created);
                INSERT INTO strategies (id, name, definition, created_at, updated_at)
                VALUES (@Strategy, 'EMA', '{}'::jsonb, @Created, @Created);
                INSERT INTO bots (id, name, strategy_id, parameter_values, category, symbol, "interval", balance, created_at, updated_at, exchange_account_id)
                VALUES (@OnTestnet, 'A', @Strategy, '{}'::jsonb, 'spot', 'BTCUSDT', '60', 1000, @Created, @Created, @Testnet),
                       (@OnMainnet, 'B', @Strategy, '{}'::jsonb, 'spot', 'BTCUSDT', '60', 1000, @Created, @Created, @Mainnet);
                """,
                new { Mainnet = mainnet, Testnet = testnet, Strategy = strategy, OnTestnet = onTestnet, OnMainnet = onMainnet, Created });
        }

        Migrate(null);

        await using NpgsqlConnection read = await _dataSource.OpenConnectionAsync();
        (await read.QueryAsync<Guid>("SELECT id FROM exchange_accounts")).ShouldBe([mainnet]);
        (await read.QuerySingleAsync<Guid?>("SELECT exchange_account_id FROM bots WHERE id = @OnTestnet", new { OnTestnet = onTestnet })).ShouldBeNull();
        (await read.QuerySingleAsync<Guid?>("SELECT exchange_account_id FROM bots WHERE id = @OnMainnet", new { OnMainnet = onMainnet })).ShouldBe(mainnet);
        (await read.QuerySingleAsync<int>(
                "SELECT count(*) FROM information_schema.columns WHERE table_name = 'exchange_accounts' AND column_name = 'environment'"))
            .ShouldBe(0);
    }

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