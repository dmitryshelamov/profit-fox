using Google.Protobuf.WellKnownTypes;

using Grpc.Core;


using ProfitFox.Infrastructure.Exchanges.TInvest;

using Shouldly;

using Tinkoff.InvestApi.V1;

using OrderType = ProfitFox.Domain.Entities.Strategies.Enums.OrderType;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.TInvest;

/// <summary>
///     Decision 6: an order goes only while the status of the share takes orders of its type and a limit price is inside the corridor, both
///     read at most every 5 s; the next session opens with the first interval of the exchange in the schedule.
/// </summary>
public class TInvestTradingStatusesTests
{
    private const string Token = TInvestTestHost.Token;

    private TInvestTestHost _host = null!;
    private TInvestTradingStatuses _statuses = null!;
    private TInvestShare _sber = null!;
    private GetTradingStatusResponse _status = null!;
    private GetOrderBookResponse _book = null!;

    [SetUp]
    public void SetUp()
    {
        _host = new TInvestTestHost();
        _statuses = new TInvestTradingStatuses(_host.Call, _host.Time);
        _sber = TInvestShares.Tradable(TInvestFixtures.Sber())!;
        _status = new GetTradingStatusResponse { ApiTradeAvailableFlag = true, LimitOrderAvailableFlag = true, MarketOrderAvailableFlag = true };
        _book = new GetOrderBookResponse { LimitDown = 240m.ToQuotation(), LimitUp = 260m.ToQuotation() };
        _host.Api
            .On<GetTradingStatusRequest, GetTradingStatusResponse>("GetTradingStatus", _ => _status)
            .On<GetOrderBookRequest, GetOrderBookResponse>("GetOrderBook", _ => _book);
    }

    [TearDown]
    public void TearDown()
    {
        _host.Dispose();
    }

    [Test]
    public async Task Limit_order_inside_the_corridor_in_the_session_may_go()
    {
        (await MayPlaceAsync(OrderType.Limit, 250.5m)).ShouldBe(TInvestPlacement.Open);

        var status = (GetTradingStatusRequest)_host.Api.Calls[0].Request;
        var book = (GetOrderBookRequest)_host.Api.Calls[1].Request;
        status.InstrumentId.ShouldBe(TInvestFixtures.SberUid);
        (book.InstrumentId, book.Depth).ShouldBe((TInvestFixtures.SberUid, 1));
    }

    // A market order has no price to keep in the corridor.
    [Test]
    public async Task Market_order_in_the_session_may_go_without_the_book()
    {
        (await MayPlaceAsync(OrderType.Market, null)).ShouldBe(TInvestPlacement.Open);

        _host.Api.Calls.Select(call => call.Method).ShouldBe(["GetTradingStatus"]);
    }

    [TestCase(false, true, true, OrderType.Limit)]
    [TestCase(true, false, true, OrderType.Limit)]
    [TestCase(true, true, false, OrderType.Market)]
    public async Task Order_its_status_takes_no_order_of_waits_for_the_session(bool api, bool limit, bool market, OrderType type)
    {
        _status = new GetTradingStatusResponse { ApiTradeAvailableFlag = api, LimitOrderAvailableFlag = limit, MarketOrderAvailableFlag = market };

        (await MayPlaceAsync(type, type == OrderType.Limit ? 250m : null)).ShouldBe(TInvestPlacement.SessionClosed);
    }

    [TestCase(239.99)]
    [TestCase(260.01)]
    public async Task Limit_price_out_of_the_corridor_waits(decimal price)
    {
        (await MayPlaceAsync(OrderType.Limit, price)).ShouldBe(TInvestPlacement.OutOfCorridor);
    }

    // An empty or a turned over corridor tells nothing: the order waits.
    [TestCase(0, 0)]
    [TestCase(260, 240)]
    public async Task Limit_order_waits_for_a_corridor_that_makes_sense(decimal down, decimal up)
    {
        _book = new GetOrderBookResponse { LimitDown = down.ToQuotation(), LimitUp = up.ToQuotation() };

        (await MayPlaceAsync(OrderType.Limit, 250m)).ShouldBe(TInvestPlacement.OutOfCorridor);
    }

    [Test]
    public async Task Limit_order_waits_for_a_book_without_a_corridor()
    {
        _book = new GetOrderBookResponse();

        (await MayPlaceAsync(OrderType.Limit, 250m)).ShouldBe(TInvestPlacement.OutOfCorridor);
    }

    [Test]
    public async Task Status_and_corridor_are_read_again_after_5_seconds()
    {
        await MayPlaceAsync(OrderType.Limit, 250m);
        _host.Time.Advance(TInvestTradingStatuses.StatusLifetime - TimeSpan.FromTicks(1));
        await MayPlaceAsync(OrderType.Limit, 250m);
        _host.Api.Calls.Count.ShouldBe(2);

        _host.Time.Advance(TimeSpan.FromTicks(1));
        await MayPlaceAsync(OrderType.Limit, 250m);

        _host.Api.Calls.Count.ShouldBe(4);
    }

    // Scenario "Заявка ночью": at 00:30 in Moscow the next session opens with the morning auction at 06:50 (03:50 UTC).
    [Test]
    public async Task Next_session_opens_with_the_first_interval_of_the_exchange()
    {
        var now = DateTimeOffset.Parse("2024-03-10T21:30:00Z");
        _host.Time.SetUtcNow(now);
        _host.Api.On<TradingSchedulesRequest, TradingSchedulesResponse>("TradingSchedules", _ => Schedule(
            Day("2024-03-10", ("regular_trading_session_main", "2024-03-10T07:00:00Z", "2024-03-10T15:40:00Z"), ("regular_trading_session_evening", "2024-03-10T16:05:00Z", "2024-03-10T20:50:00Z")),
            Day("2024-03-11", ("dealer_regular_trading_session_morning", "2024-03-11T03:00:00Z", "2024-03-11T03:50:00Z"), ("opening_auction_morning", "2024-03-11T03:50:00Z", "2024-03-11T04:00:00Z"), ("regular_trading_session_morning", "2024-03-11T04:00:00Z", "2024-03-11T06:50:00Z"))));

        (await _statuses.NextSessionStartAsync(Token, TInvestFixtures.SberExchange, CancellationToken.None)).ShouldBe(DateTimeOffset.Parse("2024-03-11T03:50:00Z"));

        var request = (TradingSchedulesRequest)_host.Api.Calls.Single().Request;
        (request.Exchange, request.From.ToUtcMoment(), request.To.ToUtcMoment()).ShouldBe((TInvestFixtures.SberExchange, now, now.AddDays(7)));
    }

    [Test]
    public async Task Days_of_the_schedule_are_read_in_their_order()
    {
        _host.Time.SetUtcNow(DateTimeOffset.Parse("2024-03-10T21:30:00Z"));
        _host.Api.On<TradingSchedulesRequest, TradingSchedulesResponse>("TradingSchedules", _ => Schedule(
            Day("2024-03-12", ("regular_trading_session_main", "2024-03-12T07:00:00Z", "2024-03-12T15:40:00Z")),
            Day("2024-03-11", ("regular_trading_session_main", "2024-03-11T07:00:00Z", "2024-03-11T15:40:00Z"))));

        (await _statuses.NextSessionStartAsync(Token, TInvestFixtures.SberExchange, CancellationToken.None)).ShouldBe(DateTimeOffset.Parse("2024-03-11T07:00:00Z"));
    }

    // The hours of the dealer are not a session of the exchange.
    [Test]
    public async Task Hours_of_the_dealer_do_not_open_a_session()
    {
        _host.Time.SetUtcNow(DateTimeOffset.Parse("2024-03-10T21:30:00Z"));
        _host.Api.On<TradingSchedulesRequest, TradingSchedulesResponse>("TradingSchedules", _ => Schedule(
            Day("2024-03-11", ("dealer_regular_trading_session_morning", "2024-03-11T03:00:00Z", "2024-03-11T03:50:00Z"), ("regular_trading_session_main", "2024-03-11T07:00:00Z", "2024-03-11T15:40:00Z"))));

        (await _statuses.NextSessionStartAsync(Token, TInvestFixtures.SberExchange, CancellationToken.None)).ShouldBe(DateTimeOffset.Parse("2024-03-11T07:00:00Z"));
    }

    // A changed dictionary of the intervals falls back to a pause of a minute, never to an order into a closed session.
    [Test]
    public async Task Day_with_an_interval_of_an_unknown_type_tells_no_session()
    {
        _host.Time.SetUtcNow(DateTimeOffset.Parse("2024-03-10T21:30:00Z"));
        _host.Api.On<TradingSchedulesRequest, TradingSchedulesResponse>("TradingSchedules", _ => Schedule(
            Day("2024-03-11", ("auction_of_the_future", "2024-03-11T03:00:00Z", "2024-03-11T03:50:00Z"), ("regular_trading_session_main", "2024-03-11T07:00:00Z", "2024-03-11T15:40:00Z"))));

        (await _statuses.NextSessionStartAsync(Token, TInvestFixtures.SberExchange, CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task Schedule_without_a_session_ahead_tells_none()
    {
        _host.Time.SetUtcNow(DateTimeOffset.Parse("2024-03-10T21:30:00Z"));
        _host.Api.On<TradingSchedulesRequest, TradingSchedulesResponse>("TradingSchedules", _ => Schedule(
            Day("2024-03-10", ("regular_trading_session_main", "2024-03-10T07:00:00Z", "2024-03-10T15:40:00Z"))));

        (await _statuses.NextSessionStartAsync(Token, TInvestFixtures.SberExchange, CancellationToken.None)).ShouldBeNull();
    }

    // Scenario "Расписание не читается"; the refusal 30003 of a date in the past (ProfitGraph) included.
    [TestCase(StatusCode.InvalidArgument, "30003")]
    [TestCase(StatusCode.Unavailable, "")]
    public async Task Schedule_that_does_not_read_tells_none(StatusCode status, string detail)
    {
        _host.Api.Fail("TradingSchedules", status, detail);

        (await _statuses.NextSessionStartAsync(Token, TInvestFixtures.SberExchange, CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task Schedule_is_kept_until_the_end_of_the_day()
    {
        _host.Time.SetUtcNow(DateTimeOffset.Parse("2024-03-10T21:30:00Z"));
        _host.Api.On<TradingSchedulesRequest, TradingSchedulesResponse>("TradingSchedules", _ => Schedule(
            Day("2024-03-11", ("regular_trading_session_main", "2024-03-11T07:00:00Z", "2024-03-11T15:40:00Z"))));

        await _statuses.NextSessionStartAsync(Token, TInvestFixtures.SberExchange, CancellationToken.None);
        _host.Time.SetUtcNow(DateTimeOffset.Parse("2024-03-10T23:59:59Z"));
        await _statuses.NextSessionStartAsync(Token, TInvestFixtures.SberExchange, CancellationToken.None);
        _host.Api.Calls.Count.ShouldBe(1);

        _host.Time.SetUtcNow(DateTimeOffset.Parse("2024-03-11T00:00:00Z"));
        await _statuses.NextSessionStartAsync(Token, TInvestFixtures.SberExchange, CancellationToken.None);

        _host.Api.Calls.Count.ShouldBe(2);
    }

    private Task<TInvestPlacement> MayPlaceAsync(OrderType type, decimal? price)
    {
        return _statuses.MayPlaceAsync(Token, _sber, type, price, CancellationToken.None);
    }

    private static TradingSchedulesResponse Schedule(params TradingDay[] days)
    {
        return new TradingSchedulesResponse { Exchanges = { new TradingSchedule { Exchange = TInvestFixtures.SberExchange, Days = { days } } } };
    }

    private static TradingDay Day(string date, params (string Type, string Start, string End)[] intervals)
    {
        return new TradingDay
        {
            Date = Timestamp.FromDateTimeOffset(DateTimeOffset.Parse($"{date}T00:00:00Z")),
            IsTradingDay = true,
            Intervals =
            {
                intervals.Select(interval => new TradingInterval
                {
                    Type = interval.Type,
                    Interval = new TradingInterval.Types.TimeInterval
                    {
                        StartTs = Timestamp.FromDateTimeOffset(DateTimeOffset.Parse(interval.Start)),
                        EndTs = Timestamp.FromDateTimeOffset(DateTimeOffset.Parse(interval.End))
                    }
                })
            }
        };
    }
}