using Microsoft.Extensions.Time.Testing;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using ProfitFox.Application.IdeaTrades.Runtime;
using ProfitFox.Application.LiveTrading;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.IdeaTrades;

/// <summary>The leases of the trades by ideas of a trading Worker are kept as those of its bots: one query renews all, one reads the changes.</summary>
public class IdeaTradeLeasesTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-11-05T10:00:00Z");

    private readonly WorkerIdentity _worker = new WorkerIdentity(Guid.NewGuid());
    private FakeTimeProvider _time = null!;
    private IIdeaTradeRepository _repository = null!;
    private IdeaTradeLeases _leases = null!;
    private CancellationTokenSource _stop = null!;
    private Task _running = null!;

    [SetUp]
    public void SetUp()
    {
        _time = new FakeTimeProvider(Start);
        _repository = Substitute.For<IIdeaTradeRepository>();
        _repository.GetChangedAsync(default, default, default).ReturnsForAnyArgs([]);
        _repository.RenewAllAsync(default, default!, default, default)
            .ReturnsForAnyArgs(call => (call.ArgAt<IReadOnlyCollection<(Guid TradeId, long Epoch)>?>(1) ?? []).Select(held => new LeasedIdeaTrade(Trade(held.TradeId), held.Epoch)).ToList());
        _leases = new IdeaTradeLeases(_repository, new TradingSettings(), new WorkerSettings(), _worker, _time);
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

    [Test]
    public async Task Leases_of_all_trades_are_renewed_in_one_call_each_interval()
    {
        Guid[] trades = [.. Enumerable.Range(0, 20).Select(_ => Guid.NewGuid())];

        foreach (Guid trade in trades)
        {
            _leases.Hold(new LeasedIdeaTrade(Trade(trade), 1), _ => { }, () => { });
        }

        await AdvanceAsync(TimeSpan.FromMinutes(1));

        await _repository.Received(1).RenewAllAsync(
            _worker.Id,
            Arg.Is<IReadOnlyCollection<(Guid TradeId, long Epoch)>>(held => held.Select(item => item.TradeId).Order().SequenceEqual(trades.Order())),
            Start + TimeSpan.FromMinutes(6),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Trade_that_a_renewal_does_not_bring_back_is_lost()
    {
        var trade = Guid.NewGuid();
        var lost = 0;
        _repository.RenewAllAsync(default, default!, default, default).ReturnsForAnyArgs([]);
        _leases.Hold(new LeasedIdeaTrade(Trade(trade), 1), _ => { }, () => lost++);

        await AdvanceAsync(TimeSpan.FromMinutes(1));

        lost.ShouldBe(1);
    }

    [Test]
    public async Task Second_take_of_a_trade_tells_the_first_it_is_lost()
    {
        var trade = Guid.NewGuid();
        var lost = 0;
        _leases.Hold(new LeasedIdeaTrade(Trade(trade), 1), _ => { }, () => lost++);

        _leases.Hold(new LeasedIdeaTrade(Trade(trade), 2), _ => { }, () => { });

        lost.ShouldBe(1);
    }

    [Test]
    public async Task Trade_written_by_the_api_is_handed_on_to_its_session_once()
    {
        var trade = Guid.NewGuid();
        var changes = new List<IdeaTrade>();
        _leases.Hold(new LeasedIdeaTrade(Trade(trade), 1), changes.Add, () => { });
        IdeaTrade cancelling = Trade(trade).RequestCancel();
        _repository.GetChangedAsync(default, default, default).ReturnsForAnyArgs([new ChangedIdeaTrade(cancelling, 1, Start.AddSeconds(5))]);

        await AdvanceAsync(TimeSpan.FromSeconds(30));

        changes.Where(change => change.CancelRequested).Count().ShouldBe(1);
    }

    [Test]
    public async Task Handed_back_trade_is_held_no_more_and_its_lease_is_shortened()
    {
        var trade = Guid.NewGuid();
        var changes = 0;
        _leases.Hold(new LeasedIdeaTrade(Trade(trade), 1), _ => changes++, () => { });

        await _leases.HandBackAsync(trade, 1);
        await AdvanceAsync(TimeSpan.FromMinutes(1));

        changes.ShouldBe(0);
        await _repository.Received().RenewAllAsync(
            _worker.Id, Arg.Is<IReadOnlyCollection<(Guid TradeId, long Epoch)>>(held => held.Single().TradeId == trade), Start.AddSeconds(30), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Failed_hand_back_does_not_throw()
    {
        _repository.RenewAllAsync(default, default!, default, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("The database is down."));

        await Should.NotThrowAsync(() => _leases.HandBackAsync(Guid.NewGuid(), 1));
    }

    [Test]
    public async Task Trade_is_given_up_before_its_lease_expires_when_renewals_fail()
    {
        _repository.RenewAllAsync(default, default!, default, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("The database is down."));
        DateTimeOffset? lostAt = null;
        _leases.Hold(new LeasedIdeaTrade(Trade(Guid.NewGuid()), 1), _ => { }, () => lostAt = _time.GetUtcNow());

        await AdvanceAsync(TimeSpan.FromMinutes(5));

        lostAt.ShouldNotBeNull();
        lostAt.Value.ShouldBeLessThanOrEqualTo(Start + TimeSpan.FromMinutes(5));
    }

    private static IdeaTrade Trade(Guid id)
    {
        return IdeaTrade.Create(id, IdeaGenerator.Generate(Start), Guid.NewGuid(), 106m, new IdeaTradeSize(0.47m, 49.82m, 0.2m, 1.84m, false), Start);
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