using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>Decision 4: the border of the coverage of a series in <c>candle_coverage</c>.</summary>
public class SeriesCoverageRepositoryTests
{
    private static readonly Instrument Sber = new Instrument(MarketCategory.Moex, "SBER");
    private static readonly DateTimeOffset Monday = DateTimeOffset.Parse("2024-03-04T00:00:00Z");

    private NpgsqlDataSource _dataSource = null!;
    private SeriesCoverageRepository _repository = null!;

    [SetUp]
    public void SetUp()
    {
        _dataSource = MarketDataDatabase.Open();
        _repository = new SeriesCoverageRepository(_dataSource);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
    }

    [Test]
    public async Task Coverage_is_written_read_and_replaced()
    {
        await _repository.UpsertAsync(Sber, Interval.OneHour, new SeriesCoverage(Monday, Monday.AddDays(1)), CancellationToken.None);
        (await _repository.GetAsync(Sber, Interval.OneHour, CancellationToken.None)).ShouldBe(new SeriesCoverage(Monday, Monday.AddDays(1)));

        await _repository.UpsertAsync(Sber, Interval.OneHour, new SeriesCoverage(Monday, Monday.AddDays(2)), CancellationToken.None);

        (await _repository.GetAsync(Sber, Interval.OneHour, CancellationToken.None)).ShouldBe(new SeriesCoverage(Monday, Monday.AddDays(2)));
    }

    [Test]
    public async Task Series_never_covered_has_no_coverage()
    {
        await _repository.UpsertAsync(Sber, Interval.OneHour, new SeriesCoverage(Monday, Monday.AddDays(1)), CancellationToken.None);

        (await _repository.GetAsync(Sber, Interval.FiveMinutes, CancellationToken.None)).ShouldBeNull();
        (await _repository.GetAsync(new Instrument(MarketCategory.Moex, "GAZP"), Interval.OneHour, CancellationToken.None)).ShouldBeNull();
    }

    // The border never says more than storage holds: the candles and the border go in one transaction.
    [Test]
    public async Task Candles_and_the_coverage_are_written_together()
    {
        var candles = new CandleRepository(_dataSource);

        await candles.UpsertAsync(Sber, Interval.OneHour, [CandleGenerator.Generate(Monday.AddHours(7))], new SeriesCoverage(Monday, Monday.AddDays(1)), CancellationToken.None);

        (await _repository.GetAsync(Sber, Interval.OneHour, CancellationToken.None)).ShouldBe(new SeriesCoverage(Monday, Monday.AddDays(1)));
        (await candles.GetOpenTimesAsync(Sber, Interval.OneHour, Monday, Monday.AddDays(1), CancellationToken.None)).ShouldBe([Monday.AddHours(7)]);
    }

    [Test]
    public async Task Coverage_without_candles_is_written_too()
    {
        var candles = new CandleRepository(_dataSource);

        await candles.UpsertAsync(Sber, Interval.OneHour, [], new SeriesCoverage(Monday, Monday.AddDays(1)), CancellationToken.None);

        (await _repository.GetAsync(Sber, Interval.OneHour, CancellationToken.None)).ShouldBe(new SeriesCoverage(Monday, Monday.AddDays(1)));
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
        (await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM candles")).ShouldBe(0);
    }
}