using Npgsql;

using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>Decision 5: live ideas, one open idea per pair × setup × type × interval × side kept by storage.</summary>
public class IdeaRepositoryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:05:00Z");

    private NpgsqlDataSource _dataSource = null!;
    private IdeaRepository _store = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dataSource = await MarketDataDatabase.CreateAsync();
        _store = new IdeaRepository(_dataSource);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
    }

    [Test]
    public async Task Ideas_are_read_back_exactly()
    {
        Idea idea = IdeaGenerator.Generate(Now, IdeaStatus.Active) with { Move = 3.123456789m };

        (await _store.AddAsync([idea], CancellationToken.None)).ShouldBe([idea]);

        (await _store.GetAsync(idea.Id, CancellationToken.None)).ShouldBe(idea);
        (await _store.GetOpenAsync(CancellationToken.None)).ShouldBe([idea]);
    }

    // Test of task 3.2: "вторая активная идея с тем же ключом отклоняется базой".
    [Test]
    public async Task Second_open_idea_with_the_same_key_is_refused_by_storage()
    {
        Idea first = IdeaGenerator.Generate(Now);
        Idea second = IdeaGenerator.Generate(Now.AddMinutes(5), IdeaStatus.Active);
        Idea otherSide = IdeaGenerator.Generate(Now.AddMinutes(5), side: IdeaSide.Short);
        await _store.AddAsync([first], CancellationToken.None);

        (await _store.AddAsync([second, otherSide], CancellationToken.None)).ShouldBe([otherSide]);

        (await _store.GetOpenAsync(CancellationToken.None)).ShouldBe([first, otherSide], ignoreOrder: true);
    }

    [Test]
    public async Task Closed_idea_leaves_its_key_to_a_new_one()
    {
        Idea first = IdeaGenerator.Generate(Now);
        await _store.AddAsync([first], CancellationToken.None);
        Idea stopped = first with { Track = first.Track with { Status = IdeaStatus.Stop, StatusAt = Now.AddMinutes(3), EnteredAt = Now, ExitPrice = 100m } };
        await _store.UpdateAsync([stopped], CancellationToken.None);
        Idea next = IdeaGenerator.Generate(Now.AddMinutes(5));

        (await _store.AddAsync([next], CancellationToken.None)).ShouldBe([next]);
        (await _store.GetAsync(first.Id, CancellationToken.None)).ShouldBe(stopped);
    }

    [Test]
    public async Task Update_writes_the_entry_and_the_levels()
    {
        Idea idea = IdeaGenerator.Generate(Now);
        await _store.AddAsync([idea], CancellationToken.None);
        Idea active = idea with
        {
            Track = idea.Track with { Plan = idea.Track.Plan with { Entry = 106m, Target = 118m }, Status = IdeaStatus.Active, StatusAt = Now.AddMinutes(1), EnteredAt = Now.AddMinutes(1) }
        };

        await _store.UpdateAsync([active], CancellationToken.None);

        (await _store.GetAsync(idea.Id, CancellationToken.None)).ShouldBe(active);
    }

    // The ideas of the API: the open ones and the ones closed in the last day, of one pair or of all.
    [Test]
    public async Task Recent_ideas_are_the_open_ones_and_the_ones_closed_since()
    {
        var sol = new Instrument(MarketCategory.Linear, "SOLUSDT");
        Idea open = IdeaGenerator.Generate(Now);
        Idea closedLately = IdeaGenerator.Generate(Now.AddHours(-30), IdeaStatus.Target, setup: IdeaSetup.Drop, statusAt: Now.AddHours(-2));
        Idea closedLongAgo = IdeaGenerator.Generate(Now.AddHours(-50), IdeaStatus.Stop, setup: IdeaSetup.EarlyDrop, statusAt: Now.AddHours(-26));
        Idea ofSol = IdeaGenerator.Generate(Now, instrument: sol);
        await _store.AddAsync([open, closedLately, closedLongAgo, ofSol], CancellationToken.None);

        (await _store.GetRecentAsync(Now.AddDays(-1), null, CancellationToken.None)).ShouldBe([open, ofSol, closedLately], ignoreOrder: true);
        (await _store.GetRecentAsync(Now.AddDays(-1), sol, CancellationToken.None)).ShouldBe([ofSol]);
    }
}