using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Scanner;
using ProfitFox.Application.Scanner.Ideas;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Fixtures.Ideas;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Scanner.Ideas;

/// <summary>The live ideas of the scanner on the impulse set of <see cref="IdeaFixtures.Setup" />, fed minute by minute as the stream closes them.</summary>
public class LiveIdeasTests
{
    private static readonly Instrument Wif = new Instrument(MarketCategory.Linear, "WIFUSDT");
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-02-29T23:30:00Z");
    private static readonly DateTimeOffset Four = DateTimeOffset.Parse("2024-03-01T04:00:00Z");
    private static readonly Instrument Sol = new Instrument(MarketCategory.Linear, "SOLUSDT");

    private IIdeaRepository _ideas = null!;
    private IIdeaVolatilityRepository _volatility = null!;
    private ScannerMarket _market = null!;
    private FakeTimeProvider _time = null!;
    private ICandleRepository _candles = null!;
    private LiveIdeas _live = null!;

    [SetUp]
    public void SetUp()
    {
        _ideas = Substitute.For<IIdeaRepository>();
        _volatility = Substitute.For<IIdeaVolatilityRepository>();
        ICandleRepository candles = _candles = Substitute.For<ICandleRepository>();
        _market = new ScannerMarket();
        _time = new FakeTimeProvider(Four);
        _market.Keep([new ScannerPair(Wif, true, CoveredUntil: Four)]);
        IReadOnlyList<IdeaCandle> minutes = IdeaFixtures.Setup("impulse");
        _market.Loaded(Wif, Start, Four, minutes.Where(minute => minute.OpenTime < Four).Select(Candle));

        // The closed candles of the warm-up come from storage, here joined from the same minutes.
        candles.GetAggregatedAsync(Wif, Arg.Any<Interval>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call => IdeaCandles.FromMinutes(call.ArgAt<Interval>(1), minutes, Start, call.ArgAt<DateTimeOffset>(3)));
        candles.GetAggregatedAsync(Sol, Arg.Any<Interval>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
        _ideas.SaveAsync(Arg.Any<IReadOnlyCollection<Idea>>(), Arg.Any<IReadOnlyCollection<Idea>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<IReadOnlyCollection<Idea>>(0).ToList());
        _live = new LiveIdeas(_ideas, _volatility, candles, _market, new IdeasSettings(), _time);
    }

    // Requirement "Буфер стопа": three spreads of the ticker, 3 × 20, are wider than k × ATR.
    [Test]
    public async Task Spread_of_the_ticker_sets_the_least_stop_buffer()
    {
        _market.Ticker(new MarketTicker(Wif, 100m, 90m, 110m));

        IReadOnlyList<Idea> created = await RunAsync(Four.AddMinutes(70));

        created.Single(idea => idea.Interval == Interval.FifteenMinutes && idea.Track.Plan.Type == IdeaType.BeyondExtreme).Track.Plan.Stop.ShouldBe(99.375m - 60m);
    }

    // Decision 5: the replay writes the percentiles once a day; the live ideas read them again every hour.
    [Test]
    public async Task Percentiles_of_volatility_are_read_once_an_hour()
    {
        await _live.RunAsync(Four, CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(59));
        await _live.RunAsync(Four, CancellationToken.None);
        await _volatility.Received(1).GetAllAsync(Arg.Any<CancellationToken>());

        _time.Advance(TimeSpan.FromMinutes(1));
        await _live.RunAsync(Four, CancellationToken.None);

        await _volatility.Received(2).GetAllAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Percentile_of_the_pair_makes_its_high_volatility()
    {
        _volatility.GetAllAsync(Arg.Any<CancellationToken>()).Returns([new IdeaVolatility(Wif, Interval.FifteenMinutes, 0.0186m)]);

        IReadOnlyList<Idea> created = await RunAsync(Four.AddMinutes(70));

        created.ShouldContain(idea => idea.Setup == IdeaSetup.HighVolatility && idea.Interval == Interval.FifteenMinutes);
    }

    // Fix after review: an idea open across a restart moves through the minutes in memory since its last change: the stop reached during the
    // downtime is a stop, though the target came later.
    [Test]
    public async Task Warm_up_stores_where_the_minutes_missed_took_an_open_idea()
    {
        Idea active = Active();
        _ideas.GetOpenAsync(Arg.Any<CancellationToken>()).Returns([active]);
        KeepSol(Flat(Four.AddDays(-1), Four, minute => minute == Four.AddMinutes(-30) ? 97m : null, minute => minute == Four.AddMinutes(-20) ? 105m : null));
        await _live.ReadWarmUpsAsync(CancellationToken.None);

        await _live.RunAsync(Four, CancellationToken.None);

        await _ideas.Received(1).SaveAsync(
            Arg.Any<IReadOnlyCollection<Idea>>(),
            Arg.Is<IReadOnlyCollection<Idea>>(ideas => ideas!.Any(idea => idea.Id == active.Id && idea.Track.Status == IdeaStatus.Stop && idea.Track.StatusAt == Four.AddMinutes(-30))),
            Arg.Any<CancellationToken>());
    }

    // Fix after review: a failed write leaves storage as it was; the live ideas start over from it, so the next minute stores the stop.
    [Test]
    public async Task Failed_write_starts_the_ideas_over_from_storage()
    {
        Idea active = Active();
        _ideas.GetOpenAsync(Arg.Any<CancellationToken>()).Returns([active]);
        KeepSol(Flat(Four.AddDays(-1), Four));
        await _live.ReadWarmUpsAsync(CancellationToken.None);
        await _live.RunAsync(Four, CancellationToken.None);
        _ideas.SaveAsync(Arg.Any<IReadOnlyCollection<Idea>>(), Arg.Any<IReadOnlyCollection<Idea>>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("database down"), call => call.ArgAt<IReadOnlyCollection<Idea>>(0).ToList());
        _market.Closed(Sol, Candle(new IdeaCandle(Four, 100m, 100m, 97m, 99m, 1m)));
        _market.Closed(Wif, Candle(IdeaFixtures.Setup("impulse").Single(minute => minute.OpenTime == Four)));
        await Should.ThrowAsync<InvalidOperationException>(() => _live.RunAsync(Four.AddMinutes(1), CancellationToken.None));

        _market.Closed(Sol, Candle(new IdeaCandle(Four.AddMinutes(1), 99m, 99m, 99m, 99m, 1m)));
        (await _live.ReadWarmUpsAsync(CancellationToken.None)).ShouldBeTrue();
        await _live.RunAsync(Four.AddMinutes(2), CancellationToken.None);

        // The write that failed and the one after it both carry the stop.
        await _ideas.Received(2).SaveAsync(
            Arg.Any<IReadOnlyCollection<Idea>>(),
            Arg.Is<IReadOnlyCollection<Idea>>(ideas => ideas!.Any(idea => idea.Id == active.Id && idea.Track.Status == IdeaStatus.Stop && idea.Track.StatusAt == Four)),
            Arg.Any<CancellationToken>());
        await _ideas.Received(2).GetOpenAsync(Arg.Any<CancellationToken>());
    }

    // Fix after review: after a failed write the pairs wait for their warm-ups; a minute before them does not fail.
    [Test]
    public async Task Minute_after_a_failed_write_waits_for_the_warm_ups()
    {
        KeepSol(Flat(Four.AddDays(-1), Four));
        await _live.ReadWarmUpsAsync(CancellationToken.None);
        await _live.RunAsync(Four, CancellationToken.None);
        _ideas.SaveAsync(Arg.Any<IReadOnlyCollection<Idea>>(), Arg.Any<IReadOnlyCollection<Idea>>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("database down"), call => call.ArgAt<IReadOnlyCollection<Idea>>(0).ToList());
        _market.Closed(Sol, Candle(new IdeaCandle(Four, 100m, 100m, 100m, 100m, 1m)));
        await Should.ThrowAsync<InvalidOperationException>(() => _live.RunAsync(Four.AddMinutes(1), CancellationToken.None));
        _market.Closed(Sol, Candle(new IdeaCandle(Four.AddMinutes(1), 100m, 100m, 100m, 100m, 1m)));

        (await _live.RunAsync(Four.AddMinutes(2), CancellationToken.None)).ShouldBeEmpty();
    }

    // Fix after review: a pair that leaves trading and comes back warms up again.
    [Test]
    public async Task Pair_that_leaves_trading_and_comes_back_warms_up_again()
    {
        KeepSol(Flat(Four.AddDays(-1), Four));
        await _live.ReadWarmUpsAsync(CancellationToken.None);
        await _live.RunAsync(Four, CancellationToken.None);
        _market.Keep([new ScannerPair(Sol, false, CoveredUntil: Four)]);
        await _live.RunAsync(Four, CancellationToken.None);

        _market.Keep([new ScannerPair(Sol, true, CoveredUntil: Four)]);

        (await _live.ReadWarmUpsAsync(CancellationToken.None)).ShouldBeTrue();
    }

    // Fix after review: a pair whose memory holds no minute yet is not warmed up, the others go on, and it is read again later.
    [Test]
    public async Task Pair_without_minutes_waits_and_the_other_pairs_go_on()
    {
        _market.Keep([new ScannerPair(Sol, true, CoveredUntil: Four)]);
        _market.Loaded(Sol, Four.AddDays(-1), Four, []);
        await _live.ReadWarmUpsAsync(CancellationToken.None);
        await _live.RunAsync(Four, CancellationToken.None);
        _market.Closed(Wif, Candle(IdeaFixtures.Setup("impulse").Single(minute => minute.OpenTime == Four)));
        _market.Closed(Sol, Candle(new IdeaCandle(Four, 100m, 100m, 100m, 100m, 1m)));

        IReadOnlyDictionary<Instrument, IReadOnlyList<PairSetup>> setups = await _live.RunAsync(Four.AddMinutes(1), CancellationToken.None);

        setups.Keys.ShouldBe([Wif]);
        (await _live.ReadWarmUpsAsync(CancellationToken.None)).ShouldBeTrue();
    }

    // Fix after review: a pair without a minute in memory is not read every minute: it is read once a minute of it is there.
    [Test]
    public async Task Pair_without_minutes_is_not_read_until_it_has_some()
    {
        _market.Keep([new ScannerPair(Sol, true, CoveredUntil: Four)]);
        _market.Loaded(Sol, Four.AddDays(-1), Four, []);
        await _live.ReadWarmUpsAsync(CancellationToken.None);
        await _live.RunAsync(Four, CancellationToken.None);

        (await _live.ReadWarmUpsAsync(CancellationToken.None)).ShouldBeFalse();
        await _candles.DidNotReceive().GetAggregatedAsync(Sol, Arg.Any<Interval>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    // Fix after review: memory holds a day of minutes; a window of a setup longer than that warms up with the minutes before it from storage.
    [Test]
    public async Task Window_of_a_setup_longer_than_a_day_warms_up_with_stored_minutes()
    {
        var settings = new IdeasSettings { ByInterval = new Dictionary<string, ThresholdOverride> { ["240"] = new ThresholdOverride { Candles = 7 } } };
        var live = new LiveIdeas(_ideas, _volatility, _candles, _market, settings, _time);
        _candles.GetAsync(Wif, Interval.OneMinute, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

        await live.ReadWarmUpsAsync(CancellationToken.None);
        await live.RunAsync(Four, CancellationToken.None);

        await _candles.Received(1).GetAsync(Wif, Interval.OneMinute, Four.AddMinutes(-7 * 240), Four, 7 * 240, Arg.Any<CancellationToken>());
    }

    // Fix after review: a warm-up read before the live ideas started over is of the old state; it is dropped and the pair is read again.
    [Test]
    public async Task Warm_up_read_before_a_start_over_is_dropped_and_read_again()
    {
        _market.Keep([new ScannerPair(Wif, false, CoveredUntil: Four)]);
        KeepSol(Flat(Four.AddDays(-1), Four));
        var gate = new TaskCompletionSource();
        _candles.GetAggregatedAsync(Sol, Arg.Any<Interval>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await gate.Task;

                return (IReadOnlyList<IdeaCandle>)[];
            });
        Task reading = _live.ReadWarmUpsAsync(CancellationToken.None);
        _ideas.SaveAsync(Arg.Any<IReadOnlyCollection<Idea>>(), Arg.Any<IReadOnlyCollection<Idea>>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("database down"), call => call.ArgAt<IReadOnlyCollection<Idea>>(0).ToList());
        await Should.ThrowAsync<InvalidOperationException>(() => _live.RunAsync(Four, CancellationToken.None));
        gate.SetResult();
        await reading;
        _market.Closed(Sol, Candle(new IdeaCandle(Four, 100m, 100m, 100m, 100m, 1m)));

        await _live.RunAsync(Four.AddMinutes(1), CancellationToken.None);

        (await _live.ReadWarmUpsAsync(CancellationToken.None)).ShouldBeTrue();
        await _live.RunAsync(Four.AddMinutes(1), CancellationToken.None);
        _market.Closed(Sol, Candle(new IdeaCandle(Four.AddMinutes(1), 100m, 100m, 100m, 100m, 1m)));
        (await _live.RunAsync(Four.AddMinutes(2), CancellationToken.None)).ShouldContainKey(Sol);
    }

    // Fix after review: a pair out of trading takes its open ideas with it: a pair the scanner no longer knows on the first minute, one that
    // leaves trading on the minute it leaves.
    [Test]
    public async Task Ideas_of_a_pair_out_of_trading_expire()
    {
        Idea active = Active();
        Idea ofDelisted = IdeaGenerator.Generate(Four.AddMinutes(-30), IdeaStatus.Active, new Instrument(MarketCategory.Spot, "OLDUSDT"));
        _ideas.GetOpenAsync(Arg.Any<CancellationToken>()).Returns([active, ofDelisted]);
        KeepSol(Flat(Four.AddDays(-1), Four));
        await _live.ReadWarmUpsAsync(CancellationToken.None);
        await _live.RunAsync(Four, CancellationToken.None);
        _market.Keep([new ScannerPair(Sol, false, CoveredUntil: Four)]);

        await _live.RunAsync(Four.AddMinutes(1), CancellationToken.None);

        await _ideas.Received(1).SaveAsync(
            Arg.Any<IReadOnlyCollection<Idea>>(),
            Arg.Is<IReadOnlyCollection<Idea>>(ideas => ideas!.Any(idea => idea.Id == active.Id && idea.Track.Status == IdeaStatus.Expired && idea.Track.ExitPrice == 100m)),
            Arg.Any<CancellationToken>());
        await _ideas.Received(1).SaveAsync(
            Arg.Any<IReadOnlyCollection<Idea>>(),
            Arg.Is<IReadOnlyCollection<Idea>>(ideas => ideas!.Any(idea => idea.Id == ofDelisted.Id && idea.Track.Status == IdeaStatus.Expired && idea.Track.ExitPrice == null)),
            Arg.Any<CancellationToken>());
    }

    // Fix after review: the ideas of a pair out of trading that was never warmed up first move through the minutes in memory, then expire
    // at the last close known.
    [Test]
    public async Task Ideas_of_a_pair_out_of_trading_move_through_the_minutes_in_memory_first()
    {
        Idea stopped = Active();
        Idea expired = Active() with { Id = Guid.NewGuid(), Setup = IdeaSetup.Drop };
        expired = expired with { Track = expired.Track with { Plan = expired.Track.Plan with { Stop = 90m } } };
        _ideas.GetOpenAsync(Arg.Any<CancellationToken>()).Returns([stopped, expired]);
        _market.Keep([new ScannerPair(Sol, false, CoveredUntil: Four)]);
        _market.Loaded(Sol, Four.AddDays(-1), Four, Flat(Four.AddDays(-1), Four, minute => minute == Four.AddMinutes(-30) ? 97m : null));
        _market.Closed(Sol, Candle(new IdeaCandle(Four, 100m, 100m, 100m, 101m, 1m)));

        await _live.RunAsync(Four.AddMinutes(1), CancellationToken.None);

        await _ideas.Received(1).SaveAsync(
            Arg.Any<IReadOnlyCollection<Idea>>(),
            Arg.Is<IReadOnlyCollection<Idea>>(ideas => ideas!.Any(idea => idea.Id == stopped.Id && idea.Track.Status == IdeaStatus.Stop && idea.Track.StatusAt == Four.AddMinutes(-30))
                && ideas!.Any(idea => idea.Id == expired.Id && idea.Track.Status == IdeaStatus.Expired && idea.Track.ExitPrice == 101m)),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Ideas_of_a_pair_out_of_trading_without_minutes_expire_without_an_exit()
    {
        Idea active = Active();
        _ideas.GetOpenAsync(Arg.Any<CancellationToken>()).Returns([active]);
        _market.Keep([new ScannerPair(Sol, false, CoveredUntil: Four)]);

        await _live.RunAsync(Four, CancellationToken.None);

        await _ideas.Received(1).SaveAsync(
            Arg.Any<IReadOnlyCollection<Idea>>(),
            Arg.Is<IReadOnlyCollection<Idea>>(ideas => ideas!.Single().Track.Status == IdeaStatus.Expired && ideas!.Single().Track.ExitPrice == null),
            Arg.Any<CancellationToken>());
    }

    // Fix after review: an idea storage refused (another open idea of its key is there) leaves the engine; the pair warms up again from storage.
    [Test]
    public async Task Idea_refused_by_storage_starts_its_pair_over()
    {
        await RunAsync(Four.AddMinutes(70), refuse: true);

        await _ideas.Received(2).GetOpenAsync(Arg.Any<CancellationToken>());
        (await _live.ReadWarmUpsAsync(CancellationToken.None)).ShouldBeTrue();
    }

    private static Idea Active()
    {
        Idea generated = IdeaGenerator.Generate(Four.AddHours(-3), IdeaStatus.Active, Sol, statusAt: Four.AddHours(-2));

        return generated with { Track = generated.Track with { Plan = new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, 104m), EnteredAt = Four.AddHours(-2) } };
    }

    private void KeepSol(IEnumerable<Candle> minutes)
    {
        _market.Keep([new ScannerPair(Sol, true, CoveredUntil: Four)]);
        _market.Loaded(Sol, Four.AddDays(-1), Four, minutes);
    }

    // Minutes of 100, with the low or high a minute is given.
    private static IEnumerable<Candle> Flat(DateTimeOffset from, DateTimeOffset to, Func<DateTimeOffset, decimal?>? low = null, Func<DateTimeOffset, decimal?>? high = null)
    {
        for (DateTimeOffset time = from; time < to; time = time.AddMinutes(1))
        {
            yield return Candle(new IdeaCandle(time, 100m, high?.Invoke(time) ?? 100m, low?.Invoke(time) ?? 100m, 100m, 1m));
        }
    }

    // Warms the pair up at 04:00 and closes its minutes one by one up to the end, running the ideas after each; storage takes every idea
    // made, or with refuse none.
    private async Task<IReadOnlyList<Idea>> RunAsync(DateTimeOffset until, bool refuse = false)
    {
        var created = new List<Idea>();
        _ideas.SaveAsync(Arg.Any<IReadOnlyCollection<Idea>>(), Arg.Any<IReadOnlyCollection<Idea>>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            created.AddRange(call.ArgAt<IReadOnlyCollection<Idea>>(0));

            return refuse ? [] : call.ArgAt<IReadOnlyCollection<Idea>>(0).ToList();
        });
        await _live.ReadWarmUpsAsync(CancellationToken.None);
        await _live.RunAsync(Four, CancellationToken.None);

        foreach (IdeaCandle minute in IdeaFixtures.Setup("impulse").Where(minute => minute.OpenTime >= Four && minute.OpenTime < until))
        {
            _market.Closed(Wif, Candle(minute));
            await _live.RunAsync(minute.OpenTime.AddMinutes(1), CancellationToken.None);
        }

        return created;
    }

    private static Candle Candle(IdeaCandle minute)
    {
        return new Candle(minute.OpenTime, new Price(minute.Open), new Price(minute.High), new Price(minute.Low), new Price(minute.Close), new Volume(1m), new Volume(minute.Volume));
    }
}