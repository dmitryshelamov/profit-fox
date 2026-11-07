using Dapper;

using FluentMigrator.Runner;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.Infrastructure.Persistence.Postgres;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>
///     Migration Plan 1 of add-burst-and-auto-limits: the ideas and trades written before have no burst, and the autos saved before take any burst
///     and have no limits, so the rows written before read as before (scenario "Автомат до лимитов").
/// </summary>
public class AddBurstAndAutoLimitsMigrationTests
{
    // The last migration before the burst and the limits of the autos.
    private const long BeforeFeatures = 202611080006;

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
        Migrate(BeforeFeatures);
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
        await _migrations.DisposeAsync();
    }

    [Test]
    public async Task New_columns_have_their_defaults()
    {
        Migrate(null);

        await using NpgsqlConnection read = await _dataSource.OpenConnectionAsync();
        IEnumerable<string> columns = await read.QueryAsync<string>(
            """
            SELECT table_name || '.' || column_name || ' ' || data_type || ' ' || is_nullable || ' ' || coalesce(column_default, '-')
            FROM information_schema.columns
            WHERE column_name IN ('burst_pairs', 'burst', 'max_per_minute_per_side', 'max_open_per_side')
              AND table_name IN ('idea_cases', 'ideas', 'idea_trades', 'idea_paper_trades', 'idea_autos')
            """);

        columns.ShouldBe(
        [
            "ideas.burst_pairs integer YES -",
            "idea_trades.burst boolean NO false",
            "idea_paper_trades.burst boolean NO false",
            "idea_autos.burst text NO 'any'::text",
            "idea_autos.max_per_minute_per_side integer YES -",
            "idea_autos.max_open_per_side integer YES -"
        ], ignoreOrder: true);
    }

    [Test]
    public async Task Auto_saved_before_takes_any_burst_and_no_limits()
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
        }

        Migrate(null);

        await using NpgsqlConnection read = await _dataSource.OpenConnectionAsync();
        (await read.QuerySingleAsync<(string, int?, int?)>("SELECT burst, max_per_minute_per_side, max_open_per_side FROM idea_autos WHERE id = @auto", new { auto }))
            .ShouldBe(("any", null, null));
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