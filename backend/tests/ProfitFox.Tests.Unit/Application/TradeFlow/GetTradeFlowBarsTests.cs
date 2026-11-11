using System.Globalization;

using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.TradeFlow.Queries.GetTradeFlowBars;
using ProfitFox.Domain.Entities.Market.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.TradeFlow;

/// <summary>Requirements "Бары покупок и продаж пары" and "Начало и разрывы данных потока сделок" (decisions 9 and 10 of collect-trade-flow).</summary>
public class GetTradeFlowBarsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-11T12:40:30Z", CultureInfo.InvariantCulture);
    private static readonly DateTimeOffset Today = DateTimeOffset.Parse("2026-11-11T00:00:00Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Sol = new Instrument(MarketCategory.Linear, "SOLUSDT");

    private ITradeFlowMinuteRepository _minutes = null!;
    private ITradeFlowDayRepository _days = null!;
    private IStreamBreakRepository _breaks = null!;
    private GetTradeFlowBarsQueryHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _minutes = Substitute.For<ITradeFlowMinuteRepository>();
        _minutes.GetBarsAsync(default!, default, default, default, default).ReturnsForAnyArgs([]);
        _days = Substitute.For<ITradeFlowDayRepository>();
        _days.GetAsync(default!, default, default, default).ReturnsForAnyArgs([]);
        _breaks = Substitute.For<IStreamBreakRepository>();
        _breaks.GetAsync(default!, default, default, default).ReturnsForAnyArgs([]);
        _handler = new GetTradeFlowBarsQueryHandler(_minutes, _days, _breaks, new FakeTimeProvider(Now));
    }

    // Scenario "Часовые бары": the period goes down to the start of its bar and is cut to the start of the current minute, whose sums come
    // only by the hub.
    [Test]
    public async Task Bars_of_the_interval_are_read_from_the_start_of_the_first_bar_to_the_current_minute()
    {
        var bar = new TradeFlowMinute(Today.AddHours(12), 1m, 2m, 3m, 4m, 5, 6);
        _minutes.GetBarsAsync(Sol, Interval.OneHour, Today.AddHours(8), Today.AddHours(12).AddMinutes(40), Arg.Any<CancellationToken>()).Returns([bar]);

        GetTradeFlowBarsResult result = await HandleAsync("linear", "60", "2026-11-11T08:30:00Z", "2026-11-11T13:00:00Z");

        result.Interval.ShouldBe(Interval.OneHour);
        result.Bars.ShouldBe([bar]);
        result.NotFound.ShouldBeFalse();
    }

    [TestCase("1", Interval.OneMinute)]
    [TestCase("5", Interval.FiveMinutes)]
    [TestCase("15", Interval.FifteenMinutes)]
    [TestCase("240", Interval.FourHours)]
    [TestCase("D", Interval.OneDay)]
    public async Task Intervals_of_the_chart_of_the_pair_are_served(string code, Interval interval)
    {
        GetTradeFlowBarsResult result = await HandleAsync("spot", code, "2026-11-11T08:00:00Z", "2026-11-11T09:00:00Z");

        result.Interval.ShouldBe(interval);
        result.Errors.ShouldBeEmpty();
    }

    // Scenario "Пара moex".
    [Test]
    public async Task Pair_of_moex_is_not_found()
    {
        GetTradeFlowBarsResult result = await HandleAsync("moex", "60", "2026-11-11T08:00:00Z", "2026-11-11T09:00:00Z");

        result.NotFound.ShouldBeTrue();
        result.Bars.ShouldBeNull();
    }

    // Scenario "Слишком длинный период", and the other wrong fields, like the candles of a pair.
    [TestCase("inverse", "60", "2026-11-11T08:00:00Z", "2026-11-11T09:00:00Z", "category")]
    [TestCase("linear", "3", "2026-11-11T08:00:00Z", "2026-11-11T09:00:00Z", "interval")]
    [TestCase("linear", null, "2026-11-11T08:00:00Z", "2026-11-11T09:00:00Z", "interval")]
    [TestCase("linear", "60", "yesterday", "2026-11-11T09:00:00Z", "from")]
    [TestCase("linear", "60", "2026-11-11T08:00:00Z", "tomorrow", "to")]
    [TestCase("linear", "60", "2026-11-11T09:00:00Z", "2026-11-11T09:00:00Z", "from")]
    [TestCase("linear", "1", "2026-11-10T00:00:00Z", "2026-11-10T16:41:00Z", "from")]
    public async Task Invalid_input_is_reported_by_parameter_name_without_reads(string category, string? interval, string from, string to, string parameter)
    {
        GetTradeFlowBarsResult result = await HandleAsync(category, interval, from, to);

        result.Bars.ShouldBeNull();
        result.Errors.Keys.ShouldBe([parameter]);
        await _minutes.DidNotReceiveWithAnyArgs().GetBarsAsync(default!, default, default, default, default);
    }

    [Test]
    public async Task Period_of_1000_minutes_is_served()
    {
        GetTradeFlowBarsResult result = await HandleAsync("linear", "1", "2026-11-10T00:00:00Z", "2026-11-10T16:40:00Z");

        result.Errors.ShouldBeEmpty();
    }

    // Scenario "Данных ещё мало".
    [Test]
    public async Task First_stored_minute_of_the_pair_is_told()
    {
        _minutes.FirstAsync(Sol, Arg.Any<CancellationToken>()).Returns(Today.AddHours(9));

        GetTradeFlowBarsResult result = await HandleAsync("linear", "60", "2026-11-10T00:00:00Z", "2026-11-11T12:00:00Z");

        result.Since.ShouldBe(Today.AddHours(9));
    }

    // Scenarios "Разрыв сегодня" and "Разрыв вчера закрыт".
    [Test]
    public async Task Breaks_of_the_flow_in_the_period_are_gaps_but_in_the_rebuilt_days_of_the_pair()
    {
        DateTimeOffset yesterday = Today.AddDays(-1);
        _breaks.GetAsync("trades", yesterday, Today.AddHours(12).AddMinutes(40), Arg.Any<CancellationToken>())
            .Returns([new StreamBreak("trades", yesterday.AddHours(14), yesterday.AddHours(14).AddMinutes(3)), new StreamBreak("trades", Today.AddHours(12).AddMinutes(2), Today.AddHours(12).AddMinutes(5))]);
        _days.GetAsync(Sol, yesterday, Today.AddHours(12).AddMinutes(40), Arg.Any<CancellationToken>())
            .Returns([new TradeFlowDay(Sol, yesterday, TradeFlowDayStatus.Rebuilt, Today)]);

        GetTradeFlowBarsResult result = await HandleAsync("linear", "60", "2026-11-10T00:00:00Z", "2026-11-11T13:00:00Z");

        result.Gaps.ShouldBe([(Today.AddHours(12).AddMinutes(2), Today.AddHours(12).AddMinutes(5))]);
    }

    // Requirement "Учёт пересобранных дней": a day the archive had no file for stays a gap.
    [Test]
    public async Task Break_in_a_missing_day_stays_a_gap()
    {
        DateTimeOffset yesterday = Today.AddDays(-1);
        var lost = new StreamBreak("trades", yesterday.AddHours(14), yesterday.AddHours(14).AddMinutes(3));
        _breaks.GetAsync(default!, default, default, default).ReturnsForAnyArgs([lost]);
        _days.GetAsync(default!, default, default, default).ReturnsForAnyArgs([new TradeFlowDay(Sol, yesterday, TradeFlowDayStatus.Missing, Today)]);

        GetTradeFlowBarsResult result = await HandleAsync("linear", "60", "2026-11-10T00:00:00Z", "2026-11-11T13:00:00Z");

        result.Gaps.ShouldBe([(lost.LostAt, lost.RestoredAt)]);
    }

    private Task<GetTradeFlowBarsResult> HandleAsync(string category, string? interval, string from, string to)
    {
        return _handler.Handle(new GetTradeFlowBarsQuery(category, "SOLUSDT", interval, from, to), CancellationToken.None);
    }
}