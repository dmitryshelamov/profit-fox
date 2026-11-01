using Npgsql;

using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies;
using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>Strategies saved before versions existed get version 1 when the service starts.</summary>
public class StrategyVersionsStartupTests
{
    private static readonly DateTimeOffset Created = DateTimeOffset.Parse("2024-03-01T00:00:00Z");

    [Test]
    public async Task Start_gives_version_one_to_a_strategy_without_versions_and_a_second_start_changes_nothing()
    {
        string connectionString = await IntegrationEnvironment.CreateDatabaseAsync();
        await using (var migrated = new ProfitFoxFactory(connectionString))
        {
            migrated.CreateClient().Dispose();
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var repository = new StrategyLibraryRepository(dataSource);
        var strategy = new Strategy(Guid.NewGuid(), "EMA", StrategyJson.ParseStrategy(StrategyFixtures.Element("ema")), Created, Created);
        (await repository.SaveAsync(new StrategyLibraryChange([strategy], [], [], [], []), 0, CancellationToken.None)).ShouldBeTrue();

        await StartAsync(connectionString);

        StrategyLibrary library = await repository.LoadAsync(CancellationToken.None);
        StrategyVersion version = library.LatestVersion(strategy.Id).ShouldNotBeNull();
        (version.Number, version.CreatedAt).ShouldBe((1, DateTimeOffset.Parse("2024-03-10T12:30:00Z")));
        library.Revision.ShouldBe(2L);

        await StartAsync(connectionString);

        StrategyLibrary again = await repository.LoadAsync(CancellationToken.None);
        again.Revision.ShouldBe(2L);
        again.LatestVersion(strategy.Id).ShouldBe(version);
    }

    private static async Task StartAsync(string connectionString)
    {
        await using var factory = new ProfitFoxFactory(connectionString);
        factory.CreateClient().Dispose();
    }
}