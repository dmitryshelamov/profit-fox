using System.Globalization;

using NSubstitute;

using ProfitFox.Application.Market.Queries.GetMarketSeries;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Market;

/// <summary>Decision 10: open interest is stored in coins and read in USDT, at the price of its time.</summary>
public class GetMarketSeriesTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-03-10T12:00:00Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Sol = new Instrument(MarketCategory.Linear, "SOLUSDT");

    private IOpenInterestRepository _openInterest = null!;
    private ICandleRepository _candles = null!;
    private GetMarketSeriesQueryHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _openInterest = Substitute.For<IOpenInterestRepository>();
        _candles = Substitute.For<ICandleRepository>();
        _handler = new GetMarketSeriesQueryHandler(_openInterest, Substitute.For<IFundingRateRepository>(), _candles);
    }

    // The price of a point is the close of the last minute before it; a minute without trades keeps the close before.
    [Test]
    public async Task Point_of_open_interest_is_worth_its_coins_at_the_close_of_the_minute_before_it()
    {
        _openInterest.GetAsync(Sol, Start, Start.AddMinutes(15), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new OpenInterestPoint(Start, 1000m), new OpenInterestPoint(Start.AddMinutes(5), 1200m), new OpenInterestPoint(Start.AddMinutes(10), 1100m)]);
        _candles.GetAsync(Sol, Interval.OneMinute, Start.AddMinutes(-1440), Start.AddMinutes(15), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Minute(Start.AddMinutes(-1), 2m), Minute(Start.AddMinutes(1), 3m), Minute(Start.AddMinutes(9), 2.5m)]);

        GetMarketSeriesResult result = await Handle();

        result.Points.ShouldBe([(Start, 2000m), (Start.AddMinutes(5), 3600m), (Start.AddMinutes(10), 2750m)]);
    }

    [Test]
    public async Task Point_with_no_minute_before_it_in_the_day_is_left_out()
    {
        _openInterest.GetAsync(Sol, Start, Start.AddMinutes(15), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new OpenInterestPoint(Start, 1000m), new OpenInterestPoint(Start.AddMinutes(5), 1200m)]);
        _candles.GetAsync(Sol, Interval.OneMinute, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Minute(Start.AddMinutes(2), 3m)]);

        GetMarketSeriesResult result = await Handle();

        result.Points.ShouldBe([(Start.AddMinutes(5), 3600m)]);
    }

    private Task<GetMarketSeriesResult> Handle()
    {
        return _handler.Handle(
            new GetMarketSeriesQuery(MarketSeries.OpenInterest, "linear", "SOLUSDT", "2024-03-10T12:00:00Z", "2024-03-10T12:15:00Z"),
            CancellationToken.None);
    }

    private static Candle Minute(DateTimeOffset openTime, decimal close)
    {
        return new Candle(openTime, new Price(close), new Price(close), new Price(close), new Price(close), new Volume(1m), new Volume(close));
    }
}