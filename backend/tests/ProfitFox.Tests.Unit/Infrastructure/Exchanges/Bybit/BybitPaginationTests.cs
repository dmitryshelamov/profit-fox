using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.Bybit;

public class BybitPaginationTests
{
    private static readonly Instrument BtcLinear = new(MarketCategory.Linear, "BTCUSDT");
    private static readonly Interval Hour = Interval.OneHour;

    [Test]
    public async Task Walks_pages_from_end_of_period_back_until_an_empty_page()
    {
        var handler = new StubHttpHandler()
            .RespondJson(BybitJson.Kline("2024-01-01T05:00:00Z", "2024-01-01T04:00:00Z"))
            .RespondJson(BybitJson.Kline("2024-01-01T03:00:00Z", "2024-01-01T02:00:00Z"))
            .RespondJson(BybitJson.Kline("2024-01-01T01:00:00Z", "2024-01-01T00:00:00Z"))
            .RespondJson(BybitJson.Kline());
        var client = BybitClients.Create(handler, TimeProvider.System);
        var from = DateTimeOffset.Parse("2023-12-31T00:00:00Z");
        var to = DateTimeOffset.Parse("2024-01-01T06:00:00Z");

        var pages = await client.GetCandlePagesAsync(BtcLinear, Hour, from, to, CancellationToken.None).ToListAsync();

        pages.Select(page => page.Count).ShouldBe(new[] { 2, 2, 2 });
        handler.Requests.Select(BybitRequests.Describe).ShouldBe(new[]
        {
            KlineQuery(from, "2024-01-01T06:00:00Z"),
            KlineQuery(from, "2024-01-01T04:00:00Z"),
            KlineQuery(from, "2024-01-01T02:00:00Z"),
            KlineQuery(from, "2024-01-01T00:00:00Z"),
        });
    }

    [Test]
    public async Task Stops_once_a_page_reaches_start_of_period()
    {
        var handler = new StubHttpHandler()
            .RespondJson(BybitJson.Kline("2024-01-01T01:00:00Z", "2024-01-01T00:00:00Z"));
        var client = BybitClients.Create(handler, TimeProvider.System);

        var pages = await client.GetCandlePagesAsync(
            BtcLinear, Hour, DateTimeOffset.Parse("2024-01-01T00:00:00Z"), DateTimeOffset.Parse("2024-01-01T02:00:00Z"), CancellationToken.None).ToListAsync();

        pages.Count.ShouldBe(1);
        handler.Requests.Count.ShouldBe(1);
    }

    // Bybit's end is inclusive, so each request ends 1 ms before the exclusive bound.
    private static string KlineQuery(DateTimeOffset from, string exclusiveEnd) => BybitRequests.Describe(
        $"/v5/market/kline?category=linear&symbol=BTCUSDT&interval=60&start={from.ToUnixTimeMilliseconds()}" +
        $"&end={DateTimeOffset.Parse(exclusiveEnd).ToUnixTimeMilliseconds() - 1}&limit=1000");
}
