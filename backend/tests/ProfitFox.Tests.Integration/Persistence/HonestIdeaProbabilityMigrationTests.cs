using Dapper;

using FluentMigrator.Runner;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.Infrastructure.Persistence.Postgres;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>Decisions 3 and 5: the buckets are cleared to be counted again, and «выше базовой» of a saved filter becomes «ожидание надёжно > 0».</summary>
public class HonestIdeaProbabilityMigrationTests
{
    // The last migration with the baseline.
    private const long WithBaseline = 202611070003;

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
        Migrate(WithBaseline);
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
        await _migrations.DisposeAsync();
    }

    [Test]
    public async Task Buckets_of_the_baseline_are_cleared()
    {
        await using (NpgsqlConnection connection = await _dataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO idea_buckets (engine_version, days, setup, type, "interval", side, category, cases, expired, probability, probability_low,
                    probability_high, baseline, expectancy)
                VALUES (1, 90, 'impulse', 'target_2r', '15', 'long', 'linear', 400, 20, 0.47, 0.42, 0.52, 0.33, 0.1)
                """);
        }

        Migrate(null);

        await using NpgsqlConnection read = await _dataSource.OpenConnectionAsync();
        (await read.ExecuteScalarAsync<long>("SELECT count(*) FROM idea_buckets")).ShouldBe(0L);
    }

    // Scenario "Перенос условия «выше базовой»": the order of the conditions stays, a filter without it does not change.
    [Test]
    public async Task Above_the_baseline_becomes_a_reliably_positive_expectancy_in_its_place()
    {
        var strong = Guid.NewGuid();
        var other = Guid.NewGuid();
        const string otherConditions = """[{"field": "change1h", "operator": ">", "value": "3", "to": null}]""";
        await using (NpgsqlConnection connection = await _dataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO saved_filters (id, name, place, conditions, created_at)
                VALUES (@Strong, 'Сильные', 'ideas',
                        '[{"field": "probability", "operator": "above_baseline", "value": null, "to": null}, {"field": "interval", "operator": "=", "value": "60", "to": null}]'::jsonb,
                        @Created),
                       (@Other, 'Рост', 'market', @OtherConditions::jsonb, @Created)
                """,
                new { Strong = strong, Other = other, OtherConditions = otherConditions, Created });
        }

        Migrate(null);

        await using NpgsqlConnection read = await _dataSource.OpenConnectionAsync();
        (await read.QuerySingleAsync<string>("SELECT conditions::text FROM saved_filters WHERE id = @Strong", new { Strong = strong })).ShouldBe(
            """[{"to": null, "field": "expectancy", "value": null, "operator": "reliably_positive"}, {"to": null, "field": "interval", "value": "60", "operator": "="}]""");
        (await read.QuerySingleAsync<bool>("SELECT conditions = @OtherConditions::jsonb FROM saved_filters WHERE id = @Other", new { Other = other, OtherConditions = otherConditions }))
            .ShouldBeTrue();
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