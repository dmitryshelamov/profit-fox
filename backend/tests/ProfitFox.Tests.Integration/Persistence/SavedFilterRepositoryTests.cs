using Npgsql;

using ProfitFox.Domain.Entities.SavedFilters;
using ProfitFox.Domain.Entities.SavedFilters.Enums;
using ProfitFox.Domain.ValueObjects.SavedFilters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.SavedFilters;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

public class SavedFilterRepositoryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:05:00Z");

    private NpgsqlDataSource _dataSource = null!;
    private SavedFilterRepository _store = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dataSource = await MarketDataDatabase.CreateAsync();
        _store = new SavedFilterRepository(_dataSource);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
    }

    [Test]
    public async Task Filter_is_added_changed_and_deleted()
    {
        SavedFilter filter = Filter("Шорт перегретых", SavedFilterPlace.Market, new FilterCondition(FilterField.Change1h, FilterOperator.Greater, 3m),
            new FilterCondition(FilterField.FundingRate, FilterOperator.Between, 0.0003m, 0.001m));
        SavedFilter other = Filter("Выше базы", SavedFilterPlace.Ideas, new FilterCondition(FilterField.Probability, FilterOperator.AboveBaseline),
            new FilterCondition(FilterField.IdeaSide, FilterOperator.Equal, Choice: "short")) with
        {
            CreatedAt = Now.AddMinutes(1)
        };
        await _store.AddAsync(filter, CancellationToken.None);
        await _store.AddAsync(other, CancellationToken.None);

        Shape(await _store.GetAllAsync(CancellationToken.None)).ShouldBe(Shape([filter, other]));

        SavedFilter changed = filter with { Name = "Перегретые", Place = SavedFilterPlace.Both, Conditions = [new FilterCondition(FilterField.Turnover24h, FilterOperator.GreaterOrEqual, 50000000m)] };
        (await _store.UpdateAsync(changed, CancellationToken.None)).ShouldBeTrue();
        Shape([(await _store.GetAsync(filter.Id, CancellationToken.None))!]).ShouldBe(Shape([changed]));

        (await _store.DeleteAsync(filter.Id, CancellationToken.None)).ShouldBeTrue();
        (await _store.GetAsync(filter.Id, CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task Change_or_delete_of_a_missing_filter_says_so()
    {
        (await _store.UpdateAsync(Filter("Нет", SavedFilterPlace.Market), CancellationToken.None)).ShouldBeFalse();
        (await _store.DeleteAsync(Guid.NewGuid(), CancellationToken.None)).ShouldBeFalse();
    }

    private static SavedFilter Filter(string name, SavedFilterPlace place, params FilterCondition[] conditions)
    {
        return new SavedFilter(Guid.NewGuid(), name, place, conditions, Now);
    }

    // The conditions are a list, which a record compares by reference.
    private static IEnumerable<string> Shape(IEnumerable<SavedFilter> filters)
    {
        return filters.Select(filter => $"{filter.Id} {filter.Name} {filter.Place} {filter.CreatedAt:O} {string.Join(';', filter.Conditions)}");
    }
}