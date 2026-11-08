using NSubstitute;

using ProfitFox.Application.MarketData.Queries.GetCandles;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.MarketData;

public class GetCandlesTests
{
    private ICandleRepository _candles = null!;
    private GetCandlesQueryHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _candles = Substitute.For<ICandleRepository>();
        _candles.GetAsync(default!, default, default, default, default, default).ReturnsForAnyArgs(Array.Empty<Candle>());
        _handler = new GetCandlesQueryHandler(_candles);
    }

    // Scenario "Интервал не из списка категории".
    [Test]
    public async Task Interval_outside_the_list_of_the_category_is_an_error_of_the_interval()
    {
        GetCandlesResult result = await _handler.Handle(new GetCandlesQuery("moex", "SBER", "720", "2024-03-01T00:00:00Z", "2024-03-10T00:00:00Z", Limit: null), CancellationToken.None);

        result.Candles.ShouldBeNull();
        result.Errors.ShouldBe(new Dictionary<string, string> { ["interval"] = "Interval must be one of the intervals of moex: 1, 3, 5, 15, 30, 60, 120, 240." });
    }

    [Test]
    public async Task Candles_of_a_moex_share_are_read_on_an_interval_of_moex()
    {
        GetCandlesResult result = await _handler.Handle(new GetCandlesQuery("moex", "sber", "60", "2024-03-01T00:00:00Z", "2024-03-10T00:00:00Z", Limit: null), CancellationToken.None);

        result.Errors.ShouldBeEmpty();
        await _candles.Received(1).GetAsync(new Instrument(MarketCategory.Moex, "SBER"), Interval.OneHour, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), 1000, Arg.Any<CancellationToken>());
    }
}