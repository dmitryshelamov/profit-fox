using Dapper;

using FluentMigrator.Runner;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.Infrastructure.Persistence.Postgres;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>Decision "Migration Plan" 1: a trade and the settings made before the autos get <c>NULL</c> in the new columns, and the new tables are empty.</summary>
public class AddIdeaAutosMigrationTests
{
    // The last migration before the autos.
    private const long BeforeAutos = 202611070010;

    private static readonly DateTimeOffset Created = DateTimeOffset.Parse("2026-11-07T10:00:00Z");

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
        Migrate(BeforeAutos);
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
        await _migrations.DisposeAsync();
    }

    [Test]
    public async Task Old_trades_and_settings_have_no_auto_and_no_guards()
    {
        var account = Guid.NewGuid();
        var trade = Guid.NewGuid();
        await using (NpgsqlConnection connection = await _dataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO exchange_accounts (id, name, api_key, secret_nonce, secret_ciphertext, secret_tag, uid, categories, created_at)
                VALUES (@Account, 'Main', 'key', '\x01', '\x02', '\x03', 100, ARRAY['linear'], @Created)
                """,
                new { Account = account, Created });
            await connection.ExecuteAsync(
                """
                INSERT INTO idea_trades (id, idea_id, account_id, category, symbol, setup, interval, type, side, entry_price, stop, target, quantity, amount, risk,
                    status, status_at, cancel_requested, close_requested, entry_quantity, entry_value, entry_fee, take_profit_quantity, take_profit_value,
                    take_profit_fee, stop_loss_quantity, stop_loss_value, stop_loss_fee, manual_quantity, manual_value, manual_fee, last_order_number, created_at,
                    lease_epoch, updated_at)
                VALUES (@Trade, gen_random_uuid(), @Account, 'linear', 'WIFUSDT', 'impulse', '15', 'target_2r', 'long', 106, 100, 115, 0.47, 49.82, 0.2,
                    'open', @Created, false, false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, @Created, 0, @Created)
                """,
                new { Trade = trade, Account = account, Created });
            await connection.ExecuteAsync("INSERT INTO idea_trading_settings (id, account_id, amount, max_amount, max_open_trades, max_total_risk, min_risk_reward) VALUES (1, NULL, 20, 300, 4, 6, 1.2)");
        }

        Migrate(null);

        await using NpgsqlConnection read = await _dataSource.OpenConnectionAsync();
        (await read.QuerySingleAsync<Guid?>("SELECT auto_id FROM idea_trades WHERE id = @trade", new { trade })).ShouldBeNull();
        (await read.QuerySingleAsync<decimal?>("SELECT daily_loss_limit FROM idea_trading_settings WHERE id = 1")).ShouldBeNull();
        (await read.QuerySingleAsync<int?>("SELECT max_same_direction FROM idea_trading_settings WHERE id = 1")).ShouldBeNull();

        foreach (string table in new[] { "idea_autos", "idea_paper_trades", "idea_auto_entries", "idea_auto_refusals" })
        {
            (await read.QuerySingleAsync<int>($"SELECT count(*)::int FROM {table}")).ShouldBe(0);
        }
    }

    // Decision 13: the name is unique among the autos that are not deleted, so the name of a deleted one is free again.
    [Test]
    public async Task Name_of_an_auto_is_unique_among_those_not_deleted()
    {
        Migrate(null);
        var account = Guid.NewGuid();
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO exchange_accounts (id, name, exchange, api_key, secret_nonce, secret_ciphertext, secret_tag, uid, categories, created_at)
            VALUES (@Account, 'Main', 'bybit', 'key', '\x01', '\x02', '\x03', 100, ARRAY['linear'], @Created)
            """,
            new { Account = account, Created });

        Task Insert(string name, DateTimeOffset? deletedAt) => connection.ExecuteAsync(
            """
            INSERT INTO idea_autos (id, name, account_id, amount, mode, setups, types, intervals, min_risk_reward, min_cases, position, created_at, deleted_at)
            VALUES (gen_random_uuid(), @name, @Account, 300, 'off', ARRAY['impulse'], ARRAY['target_2r'], ARRAY['15'], 1.2, 0, 0, @Created, @deletedAt)
            """,
            new { name, Account = account, Created, deletedAt });

        await Insert("Пробои 5м", Created);
        await Insert("Пробои 5м", null);

        await Should.ThrowAsync<PostgresException>(() => Insert("Пробои 5м", null));
    }

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