using Dapper;

using FluentMigrator.Runner;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.Infrastructure.Persistence.Postgres;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>
///     Migration Plan 1 of add-market-idea-features: the numbers of the market of cases and ideas are none, the features of trades are «нет» and
///     the conditions of the autos by features are «любой», so the rows written before read as before.
/// </summary>
public class AddMarketIdeaFeaturesMigrationTests
{
    // The last migration before the features of the market.
    private const long BeforeFeatures = 202611080003;

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
            WHERE column_name IN ('pair_change', 'btc_change', 'funding', 'day_drop', 'btc_drop', 'high_funding')
              AND table_name IN ('idea_cases', 'ideas', 'idea_trades', 'idea_paper_trades', 'idea_autos')
            """);

        columns.ShouldBe(
        [
            .. new[] { "idea_cases", "ideas" }.SelectMany(table => new[] { "pair_change", "btc_change", "funding" }.Select(column => $"{table}.{column} numeric YES -")),
            .. new[] { "idea_trades", "idea_paper_trades" }.SelectMany(table => new[] { "day_drop", "btc_drop", "high_funding" }.Select(column => $"{table}.{column} boolean NO false")),
            .. new[] { "day_drop", "btc_drop", "high_funding" }.Select(column => $"idea_autos.{column} text NO 'any'::text")
        ], ignoreOrder: true);
    }

    [Test]
    public async Task Auto_saved_before_takes_any_feature()
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
        (await read.QuerySingleAsync<string>("SELECT day_drop || btc_drop || high_funding FROM idea_autos WHERE id = @auto", new { auto })).ShouldBe("anyanyany");
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