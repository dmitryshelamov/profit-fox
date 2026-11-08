using System.Globalization;

using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Market.Queries.GetMarketCandles;
using ProfitFox.Application.MarketData;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Market;

/// <summary>Requirement "Свечи пары": the rules of the history of the bot chart.</summary>
public class GetMarketCandlesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:30:00Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Sol = new Instrument(MarketCategory.Linear, "SOLUSDT");

    private ICandleRepository _candles = null!;
    private IMarketHistoryClient _history = null!;
    private GetMarketCandlesQueryHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _candles = Substitute.For<ICandleRepository>();
        _candles.GetOpenTimesAsync(default!, default, default, default, default).ReturnsForAnyArgs([]);
        _candles.GetAsync(default!, default, default, default, default, default).ReturnsForAnyArgs(Array.Empty<Candle>());
        _history = Substitute.For<IMarketHistoryClient>();
        _history.GetCandlesOnceAsync(default!, default, default, default, default).ReturnsForAnyArgs(Array.Empty<Candle>());
        var time = new FakeTimeProvider(Now);
        _handler = new GetMarketCandlesQueryHandler(new CandleReadThrough(_candles, Substitute.For<ISeriesCoverageRepository>(), _history, time), time);
    }

    [Test]
    public async Task Candles_of_the_pair_are_read_on_the_given_interval_cut_to_the_current_candle()
    {
        GetMarketCandlesResult result = await HandleAsync("linear", "60", "2024-03-10T08:30:00Z", "2024-03-10T13:00:00Z");

        result.Interval.ShouldBe(Interval.OneHour);
        result.Candles.ShouldBe([]);
        await _candles.Received(1).GetOpenTimesAsync(
            Sol,
            Interval.OneHour,
            DateTimeOffset.Parse("2024-03-10T08:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2024-03-10T12:00:00Z", CultureInfo.InvariantCulture),
            Arg.Any<CancellationToken>());
    }

    [TestCase("inverse", "60", "2024-03-10T08:00:00Z", "2024-03-10T11:00:00Z", "category")]
    [TestCase("linear", "2", "2024-03-10T08:00:00Z", "2024-03-10T11:00:00Z", "interval")]
    [TestCase("linear", null, "2024-03-10T08:00:00Z", "2024-03-10T11:00:00Z", "interval")]
    [TestCase("linear", "60", "yesterday", "2024-03-10T11:00:00Z", "from")]
    [TestCase("linear", "60", "2024-03-10T08:00:00Z", "tomorrow", "to")]
    [TestCase("linear", "1", "2024-03-08T12:00:00Z", "2024-03-10T12:00:00Z", "from")]
    public async Task Invalid_input_is_reported_by_parameter_name_without_calls(string category, string? interval, string from, string to, string parameter)
    {
        GetMarketCandlesResult result = await HandleAsync(category, interval, from, to);

        result.Candles.ShouldBeNull();
        result.Errors.Keys.ShouldBe([parameter]);
        await _history.DidNotReceiveWithAnyArgs().GetCandlesOnceAsync(default!, default, default, default, default);
    }

    [Test]
    public async Task Unavailable_exchange_is_passed_on_with_the_stored_candles()
    {
        _history.GetCandlesOnceAsync(default!, default, default, default, default).ReturnsForAnyArgs<IReadOnlyList<Candle>>(_ => throw new ExchangeUnavailableException("down"));

        GetMarketCandlesResult result = await HandleAsync("linear", "60", "2024-03-10T08:00:00Z", "2024-03-10T11:00:00Z");

        result.Unavailable.ShouldBeTrue();
        result.Candles.ShouldBe([]);
    }

    private Task<GetMarketCandlesResult> HandleAsync(string category, string? interval, string from, string to)
    {
        return _handler.Handle(new GetMarketCandlesQuery(category, "SOLUSDT", interval, from, to), CancellationToken.None);
    }
}