using Dapper;

using FluentMigrator.Runner;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.Infrastructure.Persistence.Postgres;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>
///     Decision 8 and scenario "Исход без цены до выкатки": an outcome written before the exit price gets the exit price of its idea while the idea
///     is still stored, on the paper and on the real trades; a trade whose outcome is not written yet gets none.
/// </summary>
public class AddPaperTradeExpiryMigrationTests
{
    // The last migration before the expiry of paper trades.
    private const long BeforeExpiry = 202611080002;

    private static readonly DateTimeOffset Fired = DateTimeOffset.Parse("2026-11-07T06:00:00Z");

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
        Migrate(BeforeExpiry);
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
        await _migrations.DisposeAsync();
    }

    [Test]
    public async Task Outcome_without_a_price_gets_the_exit_price_of_its_stored_idea()
    {
        var idea = Guid.NewGuid();
        var account = Guid.NewGuid();
        var outlived = Guid.NewGuid();
        var open = Guid.NewGuid();
        var real = Guid.NewGuid();
        await using (NpgsqlConnection connection = await _dataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO ideas (id, category, symbol, setup, type, "interval", side, fired_at, entry, stop, target, risk_reward, status, status_at,
                    entered_at, exit_price, move, volume_ratio, engine_version, tick)
                VALUES (@idea, 'linear', 'WIFUSDT', 'impulse', 'target_2r', '15', 'long', @Fired, 100, 98, 104, 2, 'expired', @Expired,
                    @Fired, 101, 3.5, 2.25, 6, 0.01)
                """,
                new { idea, Fired, Expired = Fired.AddHours(2) });
            await InsertPaperTradeAsync(connection, outlived, idea, "expired");
            await InsertPaperTradeAsync(connection, open, idea, null);
            await connection.ExecuteAsync(
                """
                INSERT INTO exchange_accounts (id, name, api_key, secret_nonce, secret_ciphertext, secret_tag, uid, categories, created_at)
                VALUES (@account, 'Main', 'key', '\x01', '\x02', '\x03', 100, ARRAY['linear'], @Fired)
                """,
                new { account, Fired });
            await connection.ExecuteAsync(
                """
                INSERT INTO idea_trades (id, idea_id, account_id, category, symbol, setup, interval, type, side, entry_price, stop, target, quantity, amount, risk,
                    status, status_at, cancel_requested, close_requested, entry_quantity, entry_value, entry_fee, take_profit_quantity, take_profit_value,
                    take_profit_fee, stop_loss_quantity, stop_loss_value, stop_loss_fee, manual_quantity, manual_value, manual_fee, last_order_number, created_at,
                    lease_epoch, updated_at, idea_status, idea_status_at, idea_result)
                VALUES (@real, @idea, @account, 'linear', 'WIFUSDT', 'impulse', '15', 'target_2r', 'long', 100, 98, 104, 0.5, 50, 1,
                    'open', @Fired, false, false, 0.5, 50, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, @Fired, 0, @Fired, 'expired', @Expired, 0.4)
                """,
                new { real, idea, account, Fired, Expired = Fired.AddHours(2) });
        }

        Migrate(null);

        await using NpgsqlConnection read = await _dataSource.OpenConnectionAsync();
        (await read.QuerySingleAsync<decimal?>("SELECT idea_exit_price FROM idea_paper_trades WHERE id = @outlived", new { outlived })).ShouldBe(101m);
        (await read.QuerySingleAsync<decimal?>("SELECT idea_exit_price FROM idea_paper_trades WHERE id = @open", new { open })).ShouldBeNull();
        (await read.QuerySingleAsync<decimal?>("SELECT idea_exit_price FROM idea_trades WHERE id = @real", new { real })).ShouldBe(101m);
    }

    private static Task InsertPaperTradeAsync(NpgsqlConnection connection, Guid trade, Guid idea, string? ideaStatus)
    {
        return connection.ExecuteAsync(
            """
            INSERT INTO idea_paper_trades (id, idea_id, auto_id, account_id, category, symbol, setup, interval, type, side, entry_price, stop, target, quantity, amount, risk,
                status, status_at, entry_quantity, entry_value, entry_fee, take_profit_quantity, take_profit_value, take_profit_fee, stop_loss_quantity, stop_loss_value,
                stop_loss_fee, created_at, opened_at, stepped_until, idea_status, idea_status_at, idea_result)
            VALUES (@trade, @idea, gen_random_uuid(), gen_random_uuid(), 'linear', 'WIFUSDT', 'impulse', '15', 'target_2r', 'long', 100, 98, 104, 0.5, 50, 1,
                'open', @Fired, 0.5, 50, 0, 0, 0, 0, 0, 0, 0, @Fired, @Fired, @Fired, @ideaStatus, @StatusAt, NULL)
            """,
            new { trade, idea, Fired, ideaStatus, StatusAt = ideaStatus is null ? (DateTimeOffset?)null : Fired.AddHours(2) });
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