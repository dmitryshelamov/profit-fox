using Dapper;

using FluentMigrator.Runner;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.Infrastructure.Persistence.Postgres;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>Decision 4: settings saved with a risk get the amount 20, the risk column goes.</summary>
public class ReplaceIdeaTradingRiskWithAmountMigrationTests
{
    // The last migration before the amount replaced the risk.
    private const long BeforeAmount = 202611070007;

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
        Migrate(BeforeAmount);
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
        await _migrations.DisposeAsync();
    }

    [Test]
    public async Task Saved_settings_get_the_amount_of_20_and_lose_the_risk()
    {
        await using (NpgsqlConnection connection = await _dataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync("INSERT INTO idea_trading_settings (id, account_id, risk, max_amount, max_open_trades, max_total_risk, min_risk_reward) VALUES (1, NULL, 3, 300, 4, 6, 1.2)");
        }

        Migrate(null);

        await using NpgsqlConnection read = await _dataSource.OpenConnectionAsync();
        (await read.QuerySingleAsync<decimal>("SELECT amount FROM idea_trading_settings WHERE id = 1")).ShouldBe(20m);
        (await read.QuerySingleAsync<decimal>("SELECT max_amount FROM idea_trading_settings WHERE id = 1")).ShouldBe(300m);
        (await read.QuerySingleAsync<int>("SELECT count(*)::int FROM information_schema.columns WHERE table_name = 'idea_trading_settings' AND column_name = 'risk'")).ShouldBe(0);
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