using Microsoft.Extensions.Time.Testing;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using ProfitFox.Application.LiveTrading;
using ProfitFox.Application.LiveTrading.Runtime;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Entities.LiveTrading.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.LiveTrading;
using ProfitFox.Domain.ValueObjects.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.LiveTrading.Runtime;

/// <summary>Requirements "Продление аренд ботов" and "Команды владельца доходят до бота".</summary>
public class LiveBotLeasesTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-11-03T10:00:00Z");

    private readonly WorkerIdentity _worker = new WorkerIdentity(Guid.NewGuid());
    private FakeTimeProvider _time = null!;
    private ILiveBotRepository _repository = null!;
    private LiveBotLeases _leases = null!;
    private CancellationTokenSource _stop = null!;
    private Task _running = null!;

    [SetUp]
    public void SetUp()
    {
        _time = new FakeTimeProvider(Start);
        _repository = Substitute.For<ILiveBotRepository>();
        _repository.GetChangedAsync(default, default, default).ReturnsForAnyArgs([]);
        _repository.RenewAllAsync(default, default!, default, default)
            .ReturnsForAnyArgs(call => Held(call).Select(bot => new LeasedBot(Bot(bot.BotId), bot.Epoch)).ToList());

        // A lease of 5 minutes renewed each minute, the database polled each second.
        _leases = new LiveBotLeases(_repository, new TradingSettings(), new WorkerSettings(), _worker, _time);
        _stop = new CancellationTokenSource();
        _running = _leases.RunAsync(_stop.Token);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _stop.CancelAsync();
        await _running;
        _stop.Dispose();
    }

    // Scenario "Сто ботов".
    [Test]
    public async Task Leases_of_a_hundred_bots_are_renewed_in_one_call_each_interval()
    {
        Guid[] bots = [.. Enumerable.Range(0, 100).Select(_ => Guid.NewGuid())];

        foreach (Guid bot in bots)
        {
            _leases.Hold(new LeasedBot(Bot(bot), 1), _ => { }, () => { });
        }

        await AdvanceAsync(TimeSpan.FromMinutes(1));

        await _repository.Received(1).RenewAllAsync(
            _worker.Id,
            Arg.Is<IReadOnlyCollection<(Guid BotId, long Epoch)>>(held => held.Select(item => item.BotId).Order().SequenceEqual(bots.Order())),
            Start + TimeSpan.FromMinutes(6),
            Arg.Any<CancellationToken>());
    }

    // Scenario "Перехвачен один из ботов".
    [Test]
    public async Task Bot_missing_from_the_renewal_is_the_only_one_to_lose_its_lease()
    {
        var taken = Guid.NewGuid();
        _repository.RenewAllAsync(default, default!, default, default)
            .ReturnsForAnyArgs(call => Held(call).Where(bot => bot.BotId != taken).Select(bot => new LeasedBot(Bot(bot.BotId), bot.Epoch)).ToList());
        var lost = new List<Guid>();
        var renewed = new List<Guid>();

        foreach (Guid bot in new[] { Guid.NewGuid(), taken, Guid.NewGuid() })
        {
            _leases.Hold(new LeasedBot(Bot(bot), 1), changed => renewed.Add(changed.BotId), () => lost.Add(bot));
        }

        await AdvanceAsync(TimeSpan.FromMinutes(1));

        lost.ShouldBe([taken]);
        renewed.Count.ShouldBe(2);
    }

    // Scenario "Остановка при минутной аренде": a change of the owner reaches the session within a poll, not a renewal.
    [Test]
    public async Task Changed_bot_reaches_its_session_within_a_poll()
    {
        var bot = Guid.NewGuid();
        var changes = new List<LiveBot>();
        _leases.Hold(new LeasedBot(Bot(bot), 2), changes.Add, () => { });
        _repository.GetChangedAsync(_worker.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([new ChangedLiveBot(Bot(bot).Stop(), 2, Start)]);

        await AdvanceAsync(TimeSpan.FromSeconds(1));

        changes.Select(change => change.Status).ShouldBe([LiveBotStatus.Stopping]);
    }

    // A take of the bot this Worker lost is not its session's business, even when it holds the bot again.
    [Test]
    public async Task Change_of_another_take_does_not_reach_the_session()
    {
        var bot = Guid.NewGuid();
        var changes = new List<LiveBot>();
        _leases.Hold(new LeasedBot(Bot(bot), 2), changes.Add, () => { });
        _repository.GetChangedAsync(_worker.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([new ChangedLiveBot(Bot(bot).Stop(), 3, Start)]);

        await AdvanceAsync(TimeSpan.FromSeconds(1));

        changes.ShouldBeEmpty();
    }

    // The clocks of the API and of the Worker may differ, and an earlier write may commit later: each poll looks 30 s back.
    [Test]
    public async Task Each_poll_looks_back_from_the_latest_change_it_saw()
    {
        var bot = Guid.NewGuid();
        _leases.Hold(new LeasedBot(Bot(bot), 1), _ => { }, () => { });
        _repository.GetChangedAsync(_worker.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([new ChangedLiveBot(Bot(bot), 1, Start.AddSeconds(10))], []);

        await AdvanceAsync(TimeSpan.FromSeconds(2));

        await _repository.Received(1).GetChangedAsync(_worker.Id, Start.AddSeconds(-30), Arg.Any<CancellationToken>());
        await _repository.Received(1).GetChangedAsync(_worker.Id, Start.AddSeconds(-20), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Session_that_let_go_gets_nothing_more()
    {
        var bot = Guid.NewGuid();
        var changes = new List<LiveBot>();
        IDisposable held = _leases.Hold(new LeasedBot(Bot(bot), 1), changes.Add, () => { });
        _repository.GetChangedAsync(_worker.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([new ChangedLiveBot(Bot(bot).Stop(), 1, Start)]);

        held.Dispose();
        await AdvanceAsync(TimeSpan.FromMinutes(1));

        changes.ShouldBeEmpty();
        await _repository.DidNotReceiveWithAnyArgs().RenewAllAsync(default, default!, default, default);
    }

    // A database error on a renewal keeps the bots while their lease holds; they all go once the next try would come too late.
    [Test]
    public async Task Failed_renewals_lose_the_bots_only_before_their_lease_expires()
    {
        _repository.RenewAllAsync(default, default!, default, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("The database is down."));
        var lost = 0;
        _leases.Hold(new LeasedBot(Bot(Guid.NewGuid()), 1), _ => { }, () => lost++);
        _leases.Hold(new LeasedBot(Bot(Guid.NewGuid()), 1), _ => { }, () => lost++);

        await AdvanceAsync(TimeSpan.FromMinutes(3));
        int afterThree = lost;
        await AdvanceAsync(TimeSpan.FromMinutes(1));

        (afterThree, lost).ShouldBe((0, 2));
    }

    [Test]
    public async Task Renewed_lease_holds_the_bots_through_failures_until_it_expires()
    {
        var calls = 0;
        _repository.RenewAllAsync(default, default!, default, default)
            .ReturnsForAnyArgs(call => ++calls == 1 ? Held(call).Select(bot => new LeasedBot(Bot(bot.BotId), bot.Epoch)).ToList() : throw new InvalidOperationException("The database is down."));
        var lost = 0;
        _leases.Hold(new LeasedBot(Bot(Guid.NewGuid()), 1), _ => { }, () => lost++);

        await AdvanceAsync(TimeSpan.FromMinutes(4));
        int afterFour = lost;
        await AdvanceAsync(TimeSpan.FromMinutes(1));

        (afterFour, lost).ShouldBe((0, 1));
    }

    [Test]
    public async Task Failed_poll_is_asked_again()
    {
        var bot = Guid.NewGuid();
        var changes = new List<LiveBot>();
        _leases.Hold(new LeasedBot(Bot(bot), 1), changes.Add, () => { });
        _repository.GetChangedAsync(_worker.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("The database is down."), _ => [new ChangedLiveBot(Bot(bot).Stop(), 1, Start)]);

        await AdvanceAsync(TimeSpan.FromSeconds(2));

        changes.Select(change => change.Status).ShouldBe([LiveBotStatus.Stopping]);
    }

    // A session that ended while its renewal was on the way is not told it lost the bot.
    [Test]
    public async Task Session_that_let_go_during_a_renewal_is_not_told_it_lost()
    {
        var renewal = new TaskCompletionSource<IReadOnlyList<LeasedBot>>();
        _repository.RenewAllAsync(default, default!, default, default).ReturnsForAnyArgs(renewal.Task);
        var lost = false;
        IDisposable held = _leases.Hold(new LeasedBot(Bot(Guid.NewGuid()), 1), _ => { }, () => lost = true);
        await AdvanceAsync(TimeSpan.FromMinutes(1));

        held.Dispose();
        renewal.SetResult([]);
        await AdvanceAsync(TimeSpan.FromSeconds(1));

        lost.ShouldBeFalse();
    }

    // The session of a take that ended lets go after the Worker took the bot again: the new take stays held.
    [Test]
    public async Task Old_take_letting_go_keeps_the_new_take()
    {
        var bot = Guid.NewGuid();
        var changes = new List<LiveBot>();
        IDisposable old = _leases.Hold(new LeasedBot(Bot(bot), 1), _ => { }, () => { });
        _leases.Hold(new LeasedBot(Bot(bot), 2), changes.Add, () => { });
        _repository.GetChangedAsync(_worker.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([new ChangedLiveBot(Bot(bot).Stop(), 2, Start)]);

        old.Dispose();
        await AdvanceAsync(TimeSpan.FromSeconds(1));

        changes.Count.ShouldBe(1);
    }

    // A session that fails while it is told it lost its bot must not stop the leases of the other bots of the Worker.
    [Test]
    public async Task Failing_lost_callback_keeps_the_loop_renewing_the_others()
    {
        var taken = Guid.NewGuid();
        _repository.RenewAllAsync(default, default!, default, default)
            .ReturnsForAnyArgs(call => Held(call).Where(bot => bot.BotId != taken).Select(bot => new LeasedBot(Bot(bot.BotId), bot.Epoch)).ToList());
        var renewals = 0;
        _leases.Hold(new LeasedBot(Bot(taken), 1), _ => { }, () => throw new ObjectDisposedException("lease"));
        _leases.Hold(new LeasedBot(Bot(Guid.NewGuid()), 1), _ => renewals++, () => { });

        await AdvanceAsync(TimeSpan.FromMinutes(2));

        renewals.ShouldBe(2);
    }

    [Test]
    public async Task Failing_changed_callback_keeps_the_loop_polling()
    {
        var failing = Guid.NewGuid();
        var other = Guid.NewGuid();
        var changes = new List<DateTimeOffset>();
        _leases.Hold(new LeasedBot(Bot(failing), 1), _ => throw new InvalidOperationException("The session broke."), () => { });
        _leases.Hold(new LeasedBot(Bot(other), 1), _ => changes.Add(_time.GetUtcNow()), () => { });
        _repository.GetChangedAsync(_worker.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ => [new ChangedLiveBot(Bot(failing), 1, _time.GetUtcNow()), new ChangedLiveBot(Bot(other), 1, _time.GetUtcNow())]);

        await AdvanceAsync(TimeSpan.FromSeconds(2));

        changes.Count.ShouldBe(2);
    }

    // The Worker took the bot again before it noticed the lease of the old take expired: the old session is told at once.
    [Test]
    public async Task New_take_of_a_bot_tells_the_old_take_it_lost()
    {
        var bot = Guid.NewGuid();
        var lost = 0;
        _leases.Hold(new LeasedBot(Bot(bot), 1), _ => { }, () => lost++);

        _leases.Hold(new LeasedBot(Bot(bot), 2), _ => { }, () => { });

        lost.ShouldBe(1);
    }

    // The look back returns a row again and again: its session gets it once, and again only when it was written anew.
    [Test]
    public async Task Row_read_again_reaches_the_session_only_when_it_was_written_anew()
    {
        var bot = Guid.NewGuid();
        var changes = new List<LiveBot>();
        _leases.Hold(new LeasedBot(Bot(bot), 1), changes.Add, () => { });
        _repository.GetChangedAsync(_worker.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(
            [new ChangedLiveBot(Bot(bot), 1, Start.AddSeconds(5))],
            [new ChangedLiveBot(Bot(bot), 1, Start.AddSeconds(5))],
            [new ChangedLiveBot(Bot(bot).Stop(), 1, Start.AddSeconds(3))]);

        await AdvanceAsync(TimeSpan.FromSeconds(3));

        changes.Select(change => change.Status).ShouldBe([LiveBotStatus.Running, LiveBotStatus.Stopping]);
    }

    // A renewal that fails slowly, as on a database that does not answer, still gives the bot up before its lease expires.
    [Test]
    public async Task Slow_failing_renewals_lose_the_bot_before_its_lease_expires()
    {
        _repository.RenewAllAsync(default, default!, default, default).ReturnsForAnyArgs(_ => SlowFailureAsync());
        DateTimeOffset? lostAt = null;
        _leases.Hold(new LeasedBot(Bot(Guid.NewGuid()), 1), _ => { }, () => lostAt = _time.GetUtcNow());

        await AdvanceAsync(TimeSpan.FromMinutes(4));

        lostAt.ShouldNotBeNull();
        lostAt.Value.ShouldBeLessThanOrEqualTo(Start + TimeSpan.FromMinutes(5));
    }

    // A failed session shortens its lease after the renewal on the way, so the renewal cannot lengthen it back to five minutes.
    [Test]
    public async Task Hand_back_comes_after_a_renewal_on_the_way()
    {
        var bot = Guid.NewGuid();
        var renewal = new TaskCompletionSource<IReadOnlyList<LeasedBot>>();
        var untils = new List<(DateTimeOffset Until, bool RenewalDone)>();
        _repository.RenewAllAsync(default, default!, default, default).ReturnsForAnyArgs(call =>
        {
            untils.Add((call.ArgAt<DateTimeOffset>(2), renewal.Task.IsCompleted));

            return untils.Count == 1 ? renewal.Task : Task.FromResult<IReadOnlyList<LeasedBot>>([]);
        });
        _leases.Hold(new LeasedBot(Bot(bot), 1), _ => { }, () => { });
        await AdvanceAsync(TimeSpan.FromMinutes(1));

        Task handing = _leases.HandBackAsync(bot, 1);
        await Task.Delay(50);
        renewal.SetResult([new LeasedBot(Bot(bot), 1)]);
        await handing;

        untils.ShouldBe([(Start + TimeSpan.FromMinutes(6), false), (Start + TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(30), true)]);
    }

    [Test]
    public async Task Hand_back_of_an_old_take_keeps_the_new_take_held()
    {
        var bot = Guid.NewGuid();
        var changes = 0;
        _leases.Hold(new LeasedBot(Bot(bot), 2), _ => changes++, () => { });

        await _leases.HandBackAsync(bot, 1);
        await AdvanceAsync(TimeSpan.FromMinutes(1));

        changes.ShouldBe(1);
    }

    [Test]
    public async Task Handed_back_bot_is_held_no_more()
    {
        var bot = Guid.NewGuid();
        var changes = 0;
        _leases.Hold(new LeasedBot(Bot(bot), 1), _ => changes++, () => { });

        await _leases.HandBackAsync(bot, 1);
        await AdvanceAsync(TimeSpan.FromMinutes(1));

        changes.ShouldBe(0);
    }

    // The pause never outlasts a lease shorter than it.
    [Test]
    public async Task Hand_back_never_lengthens_a_shorter_lease()
    {
        var leases = new LiveBotLeases(
            _repository, new TradingSettings { LeaseDuration = TimeSpan.FromSeconds(10), LeaseRenewal = TimeSpan.FromSeconds(5) }, new WorkerSettings(), _worker, _time);
        var bot = Guid.NewGuid();

        await leases.HandBackAsync(bot, 1);

        await _repository.Received(1).RenewAllAsync(
            _worker.Id, Arg.Is<IReadOnlyCollection<(Guid BotId, long Epoch)>>(held => held.Single().BotId == bot), Start.AddSeconds(10), Arg.Any<CancellationToken>());
    }

    // The session ends on the failure it had; a database that does not answer only leaves the lease to run out.
    [Test]
    public async Task Failed_hand_back_does_not_throw()
    {
        _repository.RenewAllAsync(default, default!, default, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("The database is down."));

        await Should.NotThrowAsync(() => _leases.HandBackAsync(Guid.NewGuid(), 1));
    }

    // Polls that fail slowly push the next renewal back: the bot is still given up before its lease expires.
    [Test]
    public async Task Slow_failing_polls_do_not_keep_the_bot_past_its_lease()
    {
        _repository.RenewAllAsync(default, default!, default, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("The database is down."));
        _repository.GetChangedAsync(default, default, default).ReturnsForAnyArgs(_ => SlowPollFailureAsync());
        DateTimeOffset? lostAt = null;
        _leases.Hold(new LeasedBot(Bot(Guid.NewGuid()), 1), _ => { }, () => lostAt = _time.GetUtcNow());

        await AdvanceAsync(TimeSpan.FromMinutes(5));

        lostAt.ShouldNotBeNull();
        lostAt.Value.ShouldBeLessThanOrEqualTo(Start + TimeSpan.FromMinutes(5));
    }

    // As a read from a database that does not answer times out.
    private Task<IReadOnlyList<ChangedLiveBot>> SlowPollFailureAsync()
    {
        _time.Advance(TimeSpan.FromSeconds(25));

        return Task.FromException<IReadOnlyList<ChangedLiveBot>>(new InvalidOperationException("The database does not answer."));
    }

    private Task<IReadOnlyList<LeasedBot>> SlowFailureAsync()
    {
        _time.Advance(TimeSpan.FromSeconds(70));

        return Task.FromException<IReadOnlyList<LeasedBot>>(new InvalidOperationException("The database does not answer."));
    }

    // A call that only configures the substitute comes without bots.
    private static IReadOnlyCollection<(Guid BotId, long Epoch)> Held(NSubstitute.Core.CallInfo call)
    {
        return call.ArgAt<IReadOnlyCollection<(Guid BotId, long Epoch)>?>(1) ?? [];
    }

    private static LiveBot Bot(Guid id)
    {
        return LiveBot.New(id).Start(1, new Dictionary<string, NodeValue>());
    }

    /// <summary>Moves the clock a second at a time, so that each poll runs before the next one is due.</summary>
    private async Task AdvanceAsync(TimeSpan span)
    {
        for (TimeSpan moved = TimeSpan.Zero; moved < span; moved += TimeSpan.FromSeconds(1))
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(5);
        }
    }
}