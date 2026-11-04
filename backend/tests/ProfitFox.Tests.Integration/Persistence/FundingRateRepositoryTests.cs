using System.Globalization;

using Npgsql;

using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

public class FundingRateRepositoryTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-03-10T12:00:00Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Sol = new Instrument(MarketCategory.Linear, "SOLUSDT");

    private NpgsqlDataSource _dataSource = null!;
    private FundingRateRepository _store = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dataSource = await MarketDataDatabase.CreateAsync();
        _store = new FundingRateRepository(_dataSource);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
    }

    [Test]
    public async Task Values_are_stored_exactly()
    {
        FundingRatePoint[] points = [new FundingRatePoint(Start, 1234567.123456789012m), new FundingRatePoint(Start.AddMinutes(5), -0.00012345m)];

        await _store.UpsertAsync(Sol, points, CancellationToken.None);

        (await _store.GetAsync(Sol, Start, Start.AddHours(1), 1000, CancellationToken.None)).ShouldBe(points);
    }

    [Test]
    public async Task Writing_a_point_again_replaces_it_instead_of_adding_a_row()
    {
        await _store.UpsertAsync(Sol, [new FundingRatePoint(Start, 1m)], CancellationToken.None);

        await _store.UpsertAsync(Sol, [new FundingRatePoint(Start, 2m)], CancellationToken.None);

        (await _store.GetAsync(Sol, Start, Start.AddHours(1), 1000, CancellationToken.None)).ShouldBe([new FundingRatePoint(Start, 2m)]);
    }

    [Test]
    public async Task Read_takes_the_points_of_the_instrument_in_the_period_up_to_the_limit()
    {
        await _store.UpsertAsync(Sol, [.. Enumerable.Range(-1, 5).Select(i => new FundingRatePoint(Start.AddMinutes(5 * i), i))], CancellationToken.None);
        await _store.UpsertAsync(new Instrument(MarketCategory.Linear, "BTCUSDT"), [new FundingRatePoint(Start, 9m)], CancellationToken.None);

        IReadOnlyList<FundingRatePoint> all = await _store.GetAsync(Sol, Start, Start.AddMinutes(15), 1000, CancellationToken.None);
        IReadOnlyList<FundingRatePoint> limited = await _store.GetAsync(Sol, Start, Start.AddMinutes(15), 2, CancellationToken.None);

        all.ShouldBe([new FundingRatePoint(Start, 0m), new FundingRatePoint(Start.AddMinutes(5), 1m), new FundingRatePoint(Start.AddMinutes(10), 2m)]);
        limited.ShouldBe([new FundingRatePoint(Start, 0m), new FundingRatePoint(Start.AddMinutes(5), 1m)]);
    }
}