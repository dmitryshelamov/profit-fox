using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Bots.Queries.GetBotCandles;
using ProfitFox.Application.MarketData;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Bots;

public class GetBotCandlesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:30:00Z");
    private static readonly Instrument Btc = new(MarketCategory.Spot, "BTCUSDT");

    private IStrategyLibraryRepository _library = null!;
    private ICandleRepository _candles = null!;
    private IMarketHistoryClient _history = null!;
    private GetBotCandlesQueryHandler _handler = null!;
    private Bot _bot = null!;

    [SetUp]
    public void SetUp()
    {
        _bot = new Bot(Guid.NewGuid(), "EMA BTC", Guid.NewGuid(), new Dictionary<string, NodeValue>(), Btc, Interval.OneHour, 1000m, Now, Now);
        _library = Substitute.For<IStrategyLibraryRepository>();
        _library.GetBotAsync(_bot.Id, Arg.Any<CancellationToken>()).Returns(_bot);
        _candles = Substitute.For<ICandleRepository>();
        _candles.GetOpenTimesAsync(default!, default, default, default, default).ReturnsForAnyArgs([]);
        _candles.GetAsync(default!, default, default, default, default, default).ReturnsForAnyArgs(Array.Empty<Candle>());
        _history = Substitute.For<IMarketHistoryClient>();
        _history.GetCandlesOnceAsync(default!, default, default, default, default).ReturnsForAnyArgs(Array.Empty<Candle>());
        var time = new FakeTimeProvider(Now);
        _handler = new GetBotCandlesQueryHandler(_library, new CandleReadThrough(_candles, _history, time), time);
    }

    [Test]
    public async Task Unknown_bot_gives_null()
    {
        GetBotCandlesResult? result = await _handler.Handle(new GetBotCandlesQuery(Guid.NewGuid(), null, "2024-03-10T08:00:00Z", "2024-03-10T11:00:00Z"), CancellationToken.None);

        result.ShouldBeNull();
    }

    [Test]
    public async Task Interval_defaults_to_the_interval_of_the_bot()
    {
        GetBotCandlesResult result = (await HandleAsync(null, "2024-03-10T08:00:00Z", "2024-03-10T11:00:00Z"))!;

        result.Interval.ShouldBe(Interval.OneHour);
        await _candles.Received(1).GetAsync(Btc, Interval.OneHour, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Given_interval_replaces_the_interval_of_the_bot()
    {
        GetBotCandlesResult result = (await HandleAsync("5", "2024-03-10T11:00:00Z", "2024-03-10T12:00:00Z"))!;

        result.Interval.ShouldBe(Interval.FiveMinutes);
        await _candles.Received(1).GetAsync(Btc, Interval.FiveMinutes, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [TestCase("2", "2024-03-10T08:00:00Z", "2024-03-10T11:00:00Z", "interval")]
    [TestCase(null, "yesterday", "2024-03-10T11:00:00Z", "from")]
    [TestCase(null, "2024-03-10T08:00:00Z", "tomorrow", "to")]
    [TestCase(null, "2024-03-10T11:00:00Z", "2024-03-10T08:00:00Z", "from")]
    public async Task Invalid_input_is_reported_by_parameter_name_without_calls(string? interval, string from, string to, string parameter)
    {
        GetBotCandlesResult result = (await HandleAsync(interval, from, to))!;

        result.Candles.ShouldBeNull();
        result.Errors.Keys.ShouldBe([parameter]);
        await _history.DidNotReceiveWithAnyArgs().GetCandlesOnceAsync(default!, default, default, default, default);
    }

    // Requirement "Валидация параметров чтения": the interval must be in the list of the category of the bot.
    [Test]
    public async Task Interval_outside_the_list_of_the_category_of_the_bot_is_an_error_of_the_interval()
    {
        var sber = new Bot(Guid.NewGuid(), "EMA SBER", Guid.NewGuid(), new Dictionary<string, NodeValue>(), new Instrument(MarketCategory.Moex, "SBER"), Interval.OneHour, 1000m, Now, Now);
        _library.GetBotAsync(sber.Id, Arg.Any<CancellationToken>()).Returns(sber);

        GetBotCandlesResult result = (await _handler.Handle(new GetBotCandlesQuery(sber.Id, "D", "2024-03-01T08:00:00Z", "2024-03-10T11:00:00Z"), CancellationToken.None))!;

        result.Candles.ShouldBeNull();
        result.Errors.ShouldBe(new Dictionary<string, string> { ["interval"] = "Interval must be one of the intervals of moex: 1, 3, 5, 15, 30, 60, 120, 240." });
    }

    [Test]
    public async Task From_is_lowered_to_the_start_of_its_candle()
    {
        await HandleAsync(null, "2024-03-10T08:30:00Z", "2024-03-10T11:00:00Z");

        await _candles.Received(1).GetOpenTimesAsync(
            Btc,
            Interval.OneHour,
            DateTimeOffset.Parse("2024-03-10T08:00:00Z"),
            DateTimeOffset.Parse("2024-03-10T11:00:00Z"),
            Arg.Any<CancellationToken>());
    }

    // Scenario "Период до текущего момента".
    [Test]
    public async Task To_is_cut_to_the_start_of_the_current_candle()
    {
        await HandleAsync(null, "2024-03-10T08:00:00Z", "2024-03-10T13:00:00Z");

        await _candles.Received(1).GetOpenTimesAsync(
            Btc,
            Interval.OneHour,
            DateTimeOffset.Parse("2024-03-10T08:00:00Z"),
            DateTimeOffset.Parse("2024-03-10T12:00:00Z"),
            Arg.Any<CancellationToken>());
    }

    // A candle is stored only a few seconds after its close, so the answer does not claim it before.
    [Test]
    public async Task To_is_cut_before_a_candle_that_closed_less_than_the_close_margin_ago()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2024-03-10T12:00:03Z"));
        _handler = new GetBotCandlesQueryHandler(_library, new CandleReadThrough(_candles, _history, time), time);

        await HandleAsync(null, "2024-03-10T08:00:00Z", "2024-03-10T13:00:00Z");

        await _candles.Received(1).GetOpenTimesAsync(
            Btc,
            Interval.OneHour,
            DateTimeOffset.Parse("2024-03-10T08:00:00Z"),
            DateTimeOffset.Parse("2024-03-10T11:00:00Z"),
            Arg.Any<CancellationToken>());
    }

    // Scenario "Период длиннее 1000 свечей".
    [Test]
    public async Task More_than_1000_candles_is_an_error_by_from_without_calls()
    {
        GetBotCandlesResult result = (await HandleAsync("1", "2024-03-08T12:00:00Z", "2024-03-10T12:00:00Z"))!;

        result.Candles.ShouldBeNull();
        result.Errors.Keys.ShouldBe(["from"]);
        await _history.DidNotReceiveWithAnyArgs().GetCandlesOnceAsync(default!, default, default, default, default);
    }

    [Test]
    public async Task A_period_inside_the_current_candle_is_empty_without_calls()
    {
        GetBotCandlesResult result = (await HandleAsync(null, "2024-03-10T12:10:00Z", "2024-03-10T13:00:00Z"))!;

        result.Candles.ShouldBeEmpty();
        result.Unavailable.ShouldBeFalse();
        await _history.DidNotReceiveWithAnyArgs().GetCandlesOnceAsync(default!, default, default, default, default);
        await _candles.DidNotReceiveWithAnyArgs().GetAsync(default!, default, default, default, default, default);
    }

    [Test]
    public async Task Unavailable_exchange_is_passed_on_with_the_stored_candles()
    {
        _history.GetCandlesOnceAsync(default!, default, default, default, default)
            .ReturnsForAnyArgs<IReadOnlyList<Candle>>(_ => throw new ProfitFox.Application.Exceptions.ExchangeUnavailableException("down"));

        GetBotCandlesResult result = (await HandleAsync(null, "2024-03-10T08:00:00Z", "2024-03-10T11:00:00Z"))!;

        result.Unavailable.ShouldBeTrue();
        result.Candles.ShouldBe([]);
    }

    private Task<GetBotCandlesResult?> HandleAsync(string? interval, string from, string to)
    {
        return _handler.Handle(new GetBotCandlesQuery(_bot.Id, interval, from, to), CancellationToken.None);
    }
}