using Dapper;

using FluentMigrator.Runner;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.Infrastructure.Persistence.Postgres;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>
///     Migration Plan 1 of the liquidations: the trades saved before took nothing by a liquidation or an auto-deleveraging, the new statuses are taken,
///     and an execution of the exchange is shared between trades once per trade.
/// </summary>
public class AddIdeaTradeForcedExitsMigrationTests
{
    // The last migration before the liquidations.
    private const long BeforeForcedExits = 202611090001;

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
        Migrate(BeforeForcedExits);
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
        await _migrations.DisposeAsync();
    }

    [Test]
    public async Task Old_trades_took_nothing_by_the_exchange_and_take_the_new_statuses_and_shares()
    {
        var account = Guid.NewGuid();
        var trade = Guid.NewGuid();
        await using (NpgsqlConnection connection = await _dataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO exchange_accounts (id, name, exchange, api_key, secret_nonce, secret_ciphertext, secret_tag, uid, categories, created_at)
                VALUES (@Account, 'Main', 'bybit', 'key', '\x01', '\x02', '\x03', 100, ARRAY['linear'], @Created)
                """,
                new { Account = account, Created });
            await connection.ExecuteAsync(
                """
                INSERT INTO idea_trades (id, idea_id, account_id, category, symbol, setup, interval, type, side, entry_price, stop, target, quantity, amount, risk,
                    status, status_at, cancel_requested, close_requested, entry_quantity, entry_value, entry_fee, take_profit_quantity, take_profit_value,
                    take_profit_fee, stop_loss_quantity, stop_loss_value, stop_loss_fee, manual_quantity, manual_value, manual_fee, last_order_number, created_at,
                    lease_epoch, updated_at)
                VALUES (@Trade, gen_random_uuid(), @Account, 'linear', 'JCTUSDT', 'impulse', '15', 'target_2r', 'long', 0.01582, 0.0151, 0.0172, 1100, 17.402, 7.4,
                    'open', @Created, false, false, 1100, 17.402, 0.0096, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, @Created, 0, @Created)
                """,
                new { Trade = trade, Account = account, Created });
        }

        Migrate(null);

        await using NpgsqlConnection read = await _dataSource.OpenConnectionAsync();
        (await read.QuerySingleAsync<(decimal, decimal, decimal, decimal, decimal, decimal)>(
                "SELECT liquidation_quantity, liquidation_value, liquidation_fee, adl_quantity, adl_value, adl_fee FROM idea_trades WHERE id = @trade",
                new { trade }))
            .ShouldBe((0m, 0m, 0m, 0m, 0m, 0m));
        await read.ExecuteAsync("UPDATE idea_trades SET status = 'liquidated' WHERE id = @trade", new { trade });
        await read.ExecuteAsync("UPDATE idea_trades SET status = 'closed_by_adl' WHERE id = @trade", new { trade });
        await Should.ThrowAsync<PostgresException>(() => read.ExecuteAsync("UPDATE idea_trades SET status = 'gone' WHERE id = @trade", new { trade }));
        const string share = """
                             INSERT INTO idea_trade_forced_shares (exec_id, trade_id, kind, quantity, value, fee, executed_at, applied)
                             VALUES ('e-1', @trade, 'liquidation', 1100, 15.6519, 0.0086, @Created, false)
                             """;
        await read.ExecuteAsync(share, new { trade, Created });
        await Should.ThrowAsync<PostgresException>(() => read.ExecuteAsync(share, new { trade, Created }));
        await Should.ThrowAsync<PostgresException>(() => read.ExecuteAsync(share.Replace("'e-1'", "'e-2'").Replace("'liquidation'", "'manual'"), new { trade, Created }));
        await read.ExecuteAsync("DELETE FROM idea_trades WHERE id = @trade", new { trade });
        (await read.QuerySingleAsync<int>("SELECT count(*) FROM idea_trade_forced_shares")).ShouldBe(0);
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