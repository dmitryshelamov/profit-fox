using Grpc.Core;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Infrastructure.Exchanges.TInvest;

using Shouldly;

using Tinkoff.InvestApi.V1;

using Candle = ProfitFox.Domain.Entities.MarketData.Candle;
using Instrument = ProfitFox.Domain.ValueObjects.MarketData.Instrument;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.TInvest;

/// <summary>Requirement "Свечи MOEX" and decision 7: windows by interval from the end of the period, the source Exchange, complete candles only.</summary>
public class TInvestHistoryTests
{
    private static readonly Instrument Sber = new Instrument(MarketCategory.Moex, "SBER");
    private static readonly DateTimeOffset Day = DateTimeOffset.Parse("2024-03-04T00:00:00Z");

    private TInvestTestHost _host = null!;
    private List<GetCandlesRequest> _requests = null!;
    private Dictionary<DateTimeOffset, HistoricCandle[]> _candlesByWindowEnd = null!;

    [SetUp]
    public void SetUp()
    {
        _host = new TInvestTestHost();
        _host.Api.On<InstrumentRequest, ShareResponse>("ShareBy", _ => new ShareResponse { Instrument = TInvestFixtures.Sber() });
        _requests = [];
        _candlesByWindowEnd = [];
        _host.Api.On<GetCandlesRequest, GetCandlesResponse>(
            "GetCandles",
            request =>
            {
                _requests.Add(request);

                return new GetCandlesResponse { Candles = { _candlesByWindowEnd.GetValueOrDefault(request.To.ToUtcMoment(), []) } };
            });
    }

    [TearDown]
    public void TearDown()
    {
        _host.Dispose();
    }

    // Scenario "Лоты в акции": the volume is lots × lot, the turnover volume × the typical price; the source is Exchange without a limit.
    [Test]
    public async Task Candles_come_in_shares_with_the_turnover_of_the_typical_price()
    {
        _candlesByWindowEnd[Day.AddHours(10)] = [TInvestFixtures.Candle(Day.AddHours(7), close: 305m, lots: 120)];

        IReadOnlyList<Candle> candles = await _host.MarketData.GetCandlesOnceAsync(Sber, Interval.OneHour, Day.AddHours(7), Day.AddHours(10), CancellationToken.None);

        Candle candle = candles.ShouldHaveSingleItem();
        candle.OpenTime.ShouldBe(Day.AddHours(7));
        (candle.Open.Value, candle.High.Value, candle.Low.Value, candle.Close.Value).ShouldBe((304m, 307m, 303m, 305m));
        candle.Volume.Value.ShouldBe(1200m);
        candle.Turnover.Value.ShouldBe(1200m * (307m + 303m + 305m) / 3m);
        GetCandlesRequest request = _requests.ShouldHaveSingleItem();
        (request.InstrumentId, request.Interval, request.CandleSourceType, request.HasLimit).ShouldBe((TInvestFixtures.SberUid, CandleInterval.Hour, GetCandlesRequest.Types.CandleSource.Exchange, false));
        (request.From.ToUtcMoment(), request.To.ToUtcMoment()).ShouldBe((Day.AddHours(7), Day.AddHours(10)));
    }

    [Test]
    public async Task Unfinished_candle_is_dropped()
    {
        _candlesByWindowEnd[Day.AddHours(10)] = [TInvestFixtures.Candle(Day.AddHours(7), 305m, 10), TInvestFixtures.Candle(Day.AddHours(8), 306m, 10, complete: false)];

        IReadOnlyList<Candle> candles = await _host.MarketData.GetCandlesOnceAsync(Sber, Interval.OneHour, Day.AddHours(7), Day.AddHours(10), CancellationToken.None);

        candles.Select(candle => candle.OpenTime).ShouldBe([Day.AddHours(7)]);
    }

    [Test]
    public async Task Week_of_hourly_candles_is_one_window_newest_first()
    {
        _candlesByWindowEnd[Day.AddDays(7)] = [TInvestFixtures.Candle(Day.AddHours(7), 305m, 10), TInvestFixtures.Candle(Day.AddHours(8), 306m, 10)];

        List<IReadOnlyList<Candle>> pages = await _host.MarketData.GetCandlePagesAsync(Sber, Interval.OneHour, Day, Day.AddDays(7), CancellationToken.None).ToListAsync();

        pages.ShouldHaveSingleItem().Select(candle => candle.OpenTime).ShouldBe([Day.AddHours(8), Day.AddHours(7)]);
        _requests.Count.ShouldBe(1);
    }

    [Test]
    public async Task Three_months_of_hourly_candles_are_two_windows_from_the_end()
    {
        DateTimeOffset to = Day.AddDays(91);
        _candlesByWindowEnd[to] = [TInvestFixtures.Candle(to.AddHours(-5), 310m, 10)];
        _candlesByWindowEnd[to.AddDays(-90)] = [TInvestFixtures.Candle(Day.AddHours(7), 305m, 10)];

        List<IReadOnlyList<Candle>> pages = await _host.MarketData.GetCandlePagesAsync(Sber, Interval.OneHour, Day, to, CancellationToken.None).ToListAsync();

        pages.Select(page => page.Single().OpenTime).ShouldBe([to.AddHours(-5), Day.AddHours(7)]);
        _requests.Select(request => (request.From.ToUtcMoment(), request.To.ToUtcMoment())).ShouldBe([(to.AddDays(-90), to), (Day, to.AddDays(-90))]);
    }

    // A night or a weekend: a window without candles gives no page, and the walk goes on to the older windows.
    [Test]
    public async Task Empty_window_gives_no_page_and_the_walk_goes_on()
    {
        _candlesByWindowEnd[Day.AddDays(1)] = [TInvestFixtures.Candle(Day.AddHours(7), 305m, 10)];
        _candlesByWindowEnd[Day.AddDays(3)] = [TInvestFixtures.Candle(Day.AddDays(2).AddHours(7), 306m, 10)];

        List<IReadOnlyList<Candle>> pages = await _host.MarketData.GetCandlePagesAsync(Sber, Interval.OneMinute, Day, Day.AddDays(3), CancellationToken.None).ToListAsync();

        pages.Count.ShouldBe(2);
        _requests.Count.ShouldBe(3);
    }

    [TestCase(Interval.OneMinute, 1)]
    [TestCase(Interval.ThreeMinutes, 1)]
    [TestCase(Interval.FiveMinutes, 7)]
    [TestCase(Interval.FifteenMinutes, 21)]
    [TestCase(Interval.ThirtyMinutes, 21)]
    [TestCase(Interval.OneHour, 90)]
    [TestCase(Interval.TwoHours, 90)]
    [TestCase(Interval.FourHours, 90)]
    public void Window_of_an_interval(Interval interval, int days)
    {
        TInvestMarketDataClient.Windows(interval, Day, Day.AddDays(100)).First().ShouldBe((Day.AddDays(100 - days), Day.AddDays(100)));
    }

    [Test]
    public void Daily_candles_have_no_window_on_tinvest()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => TInvestMarketDataClient.Windows(Interval.OneDay, Day, Day.AddDays(1)).ToList());
    }

    // GetCandlesOnceAsync is a single attempt: the exchange that does not answer is unavailable at once.
    [Test]
    public async Task Once_makes_a_single_attempt()
    {
        _host.Api.Fail("GetCandles", StatusCode.Unavailable);

        await Should.ThrowAsync<ExchangeUnavailableException>(() => _host.MarketData.GetCandlesOnceAsync(Sber, Interval.OneHour, Day, Day.AddHours(3), CancellationToken.None));

        _host.Api.Calls.Count(call => call.Method == "GetCandles").ShouldBe(1);
    }

    [Test]
    public async Task Candles_outside_the_window_are_left_out()
    {
        _candlesByWindowEnd[Day.AddHours(10)] = [TInvestFixtures.Candle(Day.AddHours(6), 305m, 10), TInvestFixtures.Candle(Day.AddHours(10), 306m, 10), TInvestFixtures.Candle(Day.AddHours(9), 307m, 10)];

        IReadOnlyList<Candle> candles = await _host.MarketData.GetCandlesOnceAsync(Sber, Interval.OneHour, Day.AddHours(7), Day.AddHours(10), CancellationToken.None);

        candles.Select(candle => candle.OpenTime).ShouldBe([Day.AddHours(9)]);
    }
}