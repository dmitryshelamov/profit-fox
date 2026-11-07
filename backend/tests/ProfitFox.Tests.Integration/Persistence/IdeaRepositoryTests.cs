using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
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
        _dataSource = MarketDataDatabase.Open();
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
        Idea generated = IdeaGenerator.Generate(Now, IdeaStatus.Active);
        Idea idea = generated with { Move = 3.123456789m, Track = generated.Track with { Tick = 0.0001m }, PairChange = -0.0712345m, BtcChange = -0.0123m, Funding = 0.0006m, BurstPairs = 31 };

        (await _store.SaveAsync([idea], [], CancellationToken.None)).ShouldBe([idea]);

        (await _store.GetAsync(idea.Id, CancellationToken.None)).ShouldBe(idea);
        (await _store.GetOpenAsync(CancellationToken.None)).ShouldBe([idea]);
    }

    // Task 5.2 of tune-idea-rules: an idea keeps the exit rule it fired under.
    [Test]
    public async Task Idea_keeps_its_exit_rule()
    {
        Idea generated = IdeaGenerator.Generate(Now, type: IdeaType.Pullback);
        Idea idea = generated with { Track = generated.Track with { LifeCandles = 48, TargetR = 2.5m, EntryCandles = 8 } };
        Idea levels = IdeaGenerator.Generate(Now, type: IdeaType.Levels);

        await _store.SaveAsync([idea, levels], [], CancellationToken.None);

        (await _store.GetAsync(idea.Id, CancellationToken.None))!.Track.ShouldBe(idea.Track);
        (await _store.GetAsync(levels.Id, CancellationToken.None))!.Track.ShouldBe(levels.Track);
    }

    // Review of tune-idea-rules: an idea a rolled-back image of version 4 wrote has no rule columns; it reads with the defaults of its type
    // and steps.
    [Test]
    public async Task Idea_without_its_rule_reads_with_the_defaults_of_its_type()
    {
        await using (NpgsqlConnection connection = await _dataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO ideas (id, category, symbol, setup, type, "interval", side, fired_at, entry, stop, target, risk_reward, status, status_at,
                    move, volume_ratio, engine_version, tick)
                VALUES (gen_random_uuid(), 'linear', 'WIFUSDT', 'impulse', 'pullback', '15', 'long', @Now, 104, 100, 112, 2, 'awaiting_entry', @Now,
                    3.5, 2.25, 4, 0.01)
                """,
                new { Now });
        }

        Idea old = (await _store.GetOpenAsync(CancellationToken.None)).Single();
        IdeaTrack track = old.Track;

        // Task 4.2 of add-market-idea-features: an idea before the numbers of the market has no features.
        // Task 3.2 of add-burst-and-auto-limits: one before the count of the pairs of its minute has none and no burst.
        (old.PairChange, old.BtcChange, old.Funding, old.BurstPairs, old.Features).ShouldBe((null, null, null, null, default));
        (track.LifeCandles, track.TargetR, track.EntryCandles).ShouldBe((24, (decimal?)2m, (int?)4));
        IdeaTracker.Step(track, new IdeaCandle(Now.AddHours(1), 105m, 105m, 105m, 105m, 1m)).Status.ShouldBe(IdeaStatus.Expired);
    }

    // Test of task 3.2: "вторая активная идея с тем же ключом отклоняется базой".
    [Test]
    public async Task Second_open_idea_with_the_same_key_is_refused_by_storage()
    {
        Idea first = IdeaGenerator.Generate(Now);
        Idea second = IdeaGenerator.Generate(Now.AddMinutes(5), IdeaStatus.Active);
        Idea otherSide = IdeaGenerator.Generate(Now.AddMinutes(5), side: IdeaSide.Short);
        await _store.SaveAsync([first], [], CancellationToken.None);

        (await _store.SaveAsync([second, otherSide], [], CancellationToken.None)).ShouldBe([otherSide]);

        (await _store.GetOpenAsync(CancellationToken.None)).ShouldBe([first, otherSide], ignoreOrder: true);
    }

    [Test]
    public async Task Closed_idea_leaves_its_key_to_a_new_one()
    {
        Idea first = IdeaGenerator.Generate(Now);
        await _store.SaveAsync([first], [], CancellationToken.None);
        Idea stopped = first with { Track = first.Track with { Status = IdeaStatus.Stop, StatusAt = Now.AddMinutes(3), EnteredAt = Now, ExitPrice = 100m } };
        await _store.SaveAsync([], [stopped], CancellationToken.None);
        Idea next = IdeaGenerator.Generate(Now.AddMinutes(5));

        (await _store.SaveAsync([next], [], CancellationToken.None)).ShouldBe([next]);
        (await _store.GetAsync(first.Id, CancellationToken.None)).ShouldBe(stopped);
    }

    // On one minute the idea of a key closes and its setup makes a new one: one write gives the key from the one to the other.
    [Test]
    public async Task Idea_that_closes_gives_its_key_to_a_new_one_in_the_same_write()
    {
        Idea first = IdeaGenerator.Generate(Now);
        await _store.SaveAsync([first], [], CancellationToken.None);
        Idea stopped = first with { Track = first.Track with { Status = IdeaStatus.Stop, StatusAt = Now.AddMinutes(3), EnteredAt = Now, ExitPrice = 100m } };
        Idea next = IdeaGenerator.Generate(Now.AddMinutes(4));

        (await _store.SaveAsync([next], [stopped], CancellationToken.None)).ShouldBe([next]);

        (await _store.GetOpenAsync(CancellationToken.None)).ShouldBe([next]);
    }

    [Test]
    public async Task Update_writes_the_entry_and_the_levels()
    {
        Idea idea = IdeaGenerator.Generate(Now);
        await _store.SaveAsync([idea], [], CancellationToken.None);
        Idea active = idea with
        {
            Track = idea.Track with { Plan = idea.Track.Plan with { Entry = 106m, Target = 118m }, Status = IdeaStatus.Active, StatusAt = Now.AddMinutes(1), EnteredAt = Now.AddMinutes(1) }
        };

        await _store.SaveAsync([], [active], CancellationToken.None);

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
        await _store.SaveAsync([open, closedLately, closedLongAgo, ofSol], [], CancellationToken.None);

        (await _store.GetRecentAsync(Now.AddDays(-1), null, CancellationToken.None)).ShouldBe([open, ofSol, closedLately], ignoreOrder: true);
        (await _store.GetRecentAsync(Now.AddDays(-1), sol, CancellationToken.None)).ShouldBe([ofSol]);
    }

    // Requirement "Срок хранения закрытых идей": only the ideas closed before the moment go; an open one stays however old it is.
    [Test]
    public async Task Ideas_closed_before_the_moment_are_deleted()
    {
        Idea closedLongAgo = IdeaGenerator.Generate(Now.AddDays(-9), IdeaStatus.Stop, statusAt: Now.AddDays(-8));
        Idea closedLately = IdeaGenerator.Generate(Now.AddDays(-7), IdeaStatus.Target, setup: IdeaSetup.Drop, statusAt: Now.AddDays(-6));
        Idea oldOpen = IdeaGenerator.Generate(Now.AddDays(-8), IdeaStatus.Active, setup: IdeaSetup.EarlyDrop);
        await _store.SaveAsync([closedLongAgo, closedLately, oldOpen], [], CancellationToken.None);

        (await _store.DeleteClosedBeforeAsync(Now.AddDays(-7), CancellationToken.None)).ShouldBe(1);

        (await _store.GetAsync(closedLongAgo.Id, CancellationToken.None)).ShouldBeNull();
        (await _store.GetAsync(closedLately.Id, CancellationToken.None)).ShouldBe(closedLately);
        (await _store.GetAsync(oldOpen.Id, CancellationToken.None)).ShouldBe(oldOpen);
    }
}