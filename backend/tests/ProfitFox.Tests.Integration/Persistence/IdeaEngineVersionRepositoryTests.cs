using Npgsql;

using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>Decision 6 of honest-idea-intervals and requirement "Начало версии правил": the start of a version is written once.</summary>
public class IdeaEngineVersionRepositoryTests
{
    private static readonly DateTimeOffset Noon = DateTimeOffset.Parse("2026-11-07T12:00:00Z");

    private NpgsqlDataSource _dataSource = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dataSource = await MarketDataDatabase.CreateAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
    }

    // Scenario "Новая версия": version 6 taken at 12:00 keeps 12:00 after a restart at 15:00; a version never started has none.
    [Test]
    public async Task Start_of_a_version_is_the_first_one_written()
    {
        var versions = new IdeaEngineVersionRepository(_dataSource);

        await versions.StartAsync(6, Noon, CancellationToken.None);
        await versions.StartAsync(6, Noon.AddHours(3), CancellationToken.None);

        (await versions.GetStartAsync(6, CancellationToken.None)).ShouldBe(Noon);
        (await versions.GetStartAsync(7, CancellationToken.None)).ShouldBeNull();
    }
}