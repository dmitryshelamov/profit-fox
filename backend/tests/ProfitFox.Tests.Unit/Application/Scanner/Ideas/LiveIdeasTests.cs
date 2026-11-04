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

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Scanner.Ideas;

/// <summary>The live ideas of the scanner on the impulse set of <see cref="IdeaFixtures.Setup" />, fed minute by minute as the stream closes them.</summary>
public class LiveIdeasTests
{
    private static readonly Instrument Wif = new Instrument(MarketCategory.Linear, "WIFUSDT");
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-02-29T23:30:00Z");
    private static readonly DateTimeOffset Four = DateTimeOffset.Parse("2024-03-01T04:00:00Z");

    private IIdeaRepository _ideas = null!;
    private IIdeaVolatilityRepository _volatility = null!;
    private ScannerMarket _market = null!;
    private FakeTimeProvider _time = null!;
    private LiveIdeas _live = null!;

    [SetUp]
    public void SetUp()
    {
        _ideas = Substitute.For<IIdeaRepository>();
        _volatility = Substitute.For<IIdeaVolatilityRepository>();
        ICandleRepository candles = Substitute.For<ICandleRepository>();
        _market = new ScannerMarket();
        _time = new FakeTimeProvider(Four);
        _market.Keep([new ScannerPair(Wif, true, CoveredUntil: Four)]);
        IReadOnlyList<IdeaCandle> minutes = IdeaFixtures.Setup("impulse");
        _market.Loaded(Wif, Start, Four, minutes.Where(minute => minute.OpenTime < Four).Select(Candle));

        // The closed candles of the warm-up come from storage, here joined from the same minutes.
        candles.GetAggregatedAsync(Wif, Arg.Any<Interval>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call => IdeaCandles.FromMinutes(call.ArgAt<Interval>(1), minutes, Start, call.ArgAt<DateTimeOffset>(3)));
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

    // Warms the pair up at 04:00 and closes its minutes one by one up to the end, running the ideas after each.
    private async Task<IReadOnlyList<Idea>> RunAsync(DateTimeOffset until)
    {
        var created = new List<Idea>();
        _ideas.AddAsync(Arg.Any<IReadOnlyCollection<Idea>>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            created.AddRange(call.Arg<IReadOnlyCollection<Idea>>());

            return call.Arg<IReadOnlyCollection<Idea>>().ToList();
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