using Dapper;

using FluentMigrator.Runner;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.Infrastructure.Persistence.Postgres;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>Migration Plan 1 of the budgets: an auto and the settings saved before get no budget, so nothing changes until the owner sets one.</summary>
public class AddIdeaAutoBudgetsMigrationTests
{
    // The last migration before the budgets.
    private const long BeforeBudgets = 202611080001;

    private static readonly DateTimeOffset Created = DateTimeOffset.Parse("2026-11-07T10:00:00Z");

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
        Migrate(BeforeBudgets);
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
        await _migrations.DisposeAsync();
    }

    [Test]
    public async Task Old_autos_and_settings_have_no_budget()
    {
        var auto = Guid.NewGuid();
        await using (NpgsqlConnection connection = await _dataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO idea_autos (id, name, account_id, amount, mode, setups, types, intervals, min_risk_reward, min_cases, position, created_at)
                VALUES (@auto, 'Пробои 5м', gen_random_uuid(), 300, 'paper', ARRAY['impulse'], ARRAY['target_2r'], ARRAY['15'], 1.2, 0, 0, @Created)
                """,
                new { auto, Created });
            await connection.ExecuteAsync("INSERT INTO idea_trading_settings (id, account_id, amount, max_amount, max_open_trades, max_total_risk, min_risk_reward) VALUES (1, NULL, 20, 300, 4, 6, 1.2)");
        }

        Migrate(null);

        await using NpgsqlConnection read = await _dataSource.OpenConnectionAsync();
        (await read.QuerySingleAsync<decimal?>("SELECT budget FROM idea_autos WHERE id = @auto", new { auto })).ShouldBeNull();
        (await read.QuerySingleAsync<decimal?>("SELECT autos_budget FROM idea_trading_settings WHERE id = 1")).ShouldBeNull();
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