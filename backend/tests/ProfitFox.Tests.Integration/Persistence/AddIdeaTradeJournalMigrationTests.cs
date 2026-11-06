using Dapper;

using FluentMigrator.Runner;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.Infrastructure.Persistence.Postgres;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>Decision 3: a trade made before the journal gets the time it opened from the earliest fill of its entry.</summary>
public class AddIdeaTradeJournalMigrationTests
{
    // The last migration before the journal.
    private const long BeforeJournal = 202611070006;

    private static readonly DateTimeOffset Created = DateTimeOffset.Parse("2026-11-05T10:00:00Z");

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
        Migrate(BeforeJournal);
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
        await _migrations.DisposeAsync();
    }

    [Test]
    public async Task Trade_opens_at_the_earliest_fill_of_its_entry_and_one_without_fills_has_no_time()
    {
        var account = Guid.NewGuid();
        var filled = Guid.NewGuid();
        var waiting = Guid.NewGuid();
        await using (NpgsqlConnection connection = await _dataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO exchange_accounts (id, name, api_key, secret_nonce, secret_ciphertext, secret_tag, uid, categories, created_at)
                VALUES (@Account, 'Main', 'key', '\x01', '\x02', '\x03', 100, ARRAY['linear'], @Created)
                """,
                new { Account = account, Created });

            foreach (Guid trade in new[] { filled, waiting })
            {
                await InsertTradeAsync(connection, trade, account);
                await InsertOrderAsync(connection, trade, 1, "entry", $"entry-{trade}");
            }

            await InsertOrderAsync(connection, filled, 2, "close", "close-1");
            await InsertFillAsync(connection, filled, "e-2", $"entry-{filled}", Created.AddMinutes(2));
            await InsertFillAsync(connection, filled, "e-1", $"entry-{filled}", Created.AddMinutes(1));
            await InsertFillAsync(connection, filled, "e-3", "close-1", Created.AddMinutes(0.5));
        }

        Migrate(null);

        await using NpgsqlConnection read = await _dataSource.OpenConnectionAsync();
        (await read.QuerySingleAsync<DateTime?>("SELECT opened_at FROM idea_trades WHERE id = @filled", new { filled })).ShouldBe(Created.AddMinutes(1).UtcDateTime);
        (await read.QuerySingleAsync<DateTime?>("SELECT opened_at FROM idea_trades WHERE id = @waiting", new { waiting })).ShouldBeNull();
        (await read.QuerySingleAsync<string?>("SELECT promised_badge FROM idea_trades WHERE id = @filled", new { filled })).ShouldBeNull();
    }

    private static Task InsertTradeAsync(NpgsqlConnection connection, Guid trade, Guid account)
    {
        return connection.ExecuteAsync(
            """
            INSERT INTO idea_trades (id, idea_id, account_id, category, symbol, setup, interval, type, side, entry_price, stop, target, quantity, amount, risk,
                status, status_at, cancel_requested, close_requested, entry_quantity, entry_value, entry_fee, take_profit_quantity, take_profit_value,
                take_profit_fee, stop_loss_quantity, stop_loss_value, stop_loss_fee, manual_quantity, manual_value, manual_fee, last_order_number, created_at,
                lease_epoch, updated_at)
            VALUES (@Trade, gen_random_uuid(), @Account, 'linear', 'WIFUSDT', 'impulse', '15', 'target_2r', 'long', 106, 100, 115, 0.47, 49.82, 0.2,
                'open', @Created, false, false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, @Created, 0, @Created)
            """,
            new { Trade = trade, Account = account, Created });
    }

    private static Task InsertOrderAsync(NpgsqlConnection connection, Guid trade, int number, string role, string exchangeOrderId)
    {
        return connection.ExecuteAsync(
            """
            INSERT INTO idea_trade_orders (trade_id, number, role, side, type, time_in_force, quantity, reduce_only, status, filled_quantity, filled_value, fee,
                exchange_order_id, attempts, created_at, updated_at)
            VALUES (@Trade, @Number, @Role, 'buy', 'market', 'gtc', 0.47, false, 'filled', 0, 0, 0, @ExchangeOrderId, 1, @Created, @Created)
            """,
            new { Trade = trade, Number = number, Role = role, ExchangeOrderId = exchangeOrderId, Created });
    }

    private static Task InsertFillAsync(NpgsqlConnection connection, Guid trade, string execId, string exchangeOrderId, DateTimeOffset at)
    {
        return connection.ExecuteAsync(
            "INSERT INTO idea_trade_fills (exec_id, trade_id, exchange_order_id, quantity, price, fee, fee_asset, executed_at) VALUES (@ExecId, @Trade, @ExchangeOrderId, 0.1, 106, 0.01, 'quote', @At)",
            new { ExecId = execId, Trade = trade, ExchangeOrderId = exchangeOrderId, At = at });
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