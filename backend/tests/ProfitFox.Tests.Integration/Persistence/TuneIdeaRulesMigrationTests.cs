using Dapper;

using FluentMigrator.Runner;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Infrastructure.Persistence.Postgres;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>Task 5.1 of tune-idea-rules on a database with the data of version 4: its rows stay readable, and the live ideas take their defaults.</summary>
public class TuneIdeaRulesMigrationTests
{
    // The last migration before the exit rules.
    private const long Before = 202611070006;

    private static readonly DateTimeOffset Fired = DateTimeOffset.Parse("2024-03-10T12:00:00Z");

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
        Migrate(Before);
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
        await _migrations.DisposeAsync();
    }

    // Migration plan, step 2: the live ideas of version 4 live on by the defaults of their type.
    [Test]
    public async Task Ideas_of_version_4_take_the_default_rules_of_their_types()
    {
        await using (NpgsqlConnection connection = await _dataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO ideas (id, category, symbol, setup, type, "interval", side, fired_at, entry, stop, target, risk_reward, status, status_at,
                    entered_at, exit_price, move, volume_ratio, engine_version, tick)
                SELECT gen_random_uuid(), 'linear', 'WIFUSDT', 'impulse', type, '15', 'long', @Fired, 105, 100, 115, 2, 'awaiting_entry', @Fired, NULL, NULL,
                    3.5, 2.25, 4, 0.01
                FROM unnest(ARRAY['levels', 'target_2r', 'pullback', 'stop_entry']) AS type
                """,
                new { Fired });
        }

        Migrate(null);

        IReadOnlyList<Idea> open = await new IdeaRepository(_dataSource).GetOpenAsync(CancellationToken.None);
        open.Select(idea => (idea.Track.Plan.Type.ToString(), idea.Track.LifeCandles, idea.Track.TargetR, idea.Track.EntryCandles)).ShouldBe(
            [("Levels", 24, null, null), ("TwoR", 24, 2m, null), ("Pullback", 24, 2m, 4), ("StopEntry", 24, 2m, 12)],
            ignoreOrder: true);
    }

    // Migration plan, steps 2 and 3: a case of version 4 stays as it was beside a signal of version 5, which leaves the outcome empty.
    [Test]
    public async Task Cases_of_version_4_stay_beside_the_signals_of_version_5()
    {
        await using (NpgsqlConnection connection = await _dataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO idea_cases (category, symbol, setup, type, "interval", side, fired_at, engine_version, entry, stop, target, risk_reward, status,
                    status_at, entered_at, exit_price, move, volume_ratio, result)
                VALUES ('linear', 'WIFUSDT', 'impulse', 'target_2r', '15', 'long', @Fired, 4, 105, 100, 115, 2, 'target', @Fired, @Fired, 115, 3.5, 2.25, 1.9);
                INSERT INTO idea_replay (category, symbol, engine_version, replayed_until) VALUES ('linear', 'WIFUSDT', 4, @Fired);
                """,
                new { Fired });
        }

        Migrate(null);

        await new IdeaSignalRepository(_dataSource).UpsertAsync([IdeaSignalGenerator.Generate(Fired, engineVersion: 5)], CancellationToken.None);
        await using NpgsqlConnection read = await _dataSource.OpenConnectionAsync();
        (await read.QueryAsync<(int, string?, decimal?)>("SELECT engine_version, status, result FROM idea_cases ORDER BY engine_version")).ShouldBe(
            [(4, "target", 1.9m), (5, null, null)]);
        (await read.QuerySingleAsync<string>("SELECT open_signals::text FROM idea_replay")).ShouldBe("[]");
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