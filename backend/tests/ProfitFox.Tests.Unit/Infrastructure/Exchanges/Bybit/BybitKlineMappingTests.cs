using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.Bybit;

public class BybitKlineMappingTests
{
    private static readonly Instrument BtcLinear = new(MarketCategory.Linear, "BTCUSDT");
    private static readonly DateTimeOffset From = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
    private static readonly DateTimeOffset To = DateTimeOffset.Parse("2024-01-01T02:00:00Z");

    [Test]
    public async Task Candles_keep_exact_decimal_values()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson("""
            {
              "retCode": 0,
              "retMsg": "OK",
              "result": {
                "category": "linear",
                "symbol": "BTCUSDT",
                "list": [
                  ["1704070800000", "42200", "42300.5", "42100", "42123.45", "0.000123", "5.18"],
                  ["1704067200000", "42000.1", "42250", "41950", "42200", "12.5", "525000.75"]
                ]
              },
              "retExtInfo": {},
              "time": 1704074400000
            }
            """);

        List<IReadOnlyList<Candle>> pages = await BybitClients.Create(handler, TimeProvider.System).GetCandlePagesAsync(BtcLinear, Interval.OneHour, From, To, CancellationToken.None).ToListAsync();

        pages.Single().ShouldBe(new[]
        {
            new Candle(
                DateTimeOffset.Parse("2024-01-01T01:00:00Z"),
                new Price(42200m), new Price(42300.5m), new Price(42100m), new Price(42123.45m), new Volume(0.000123m), new Volume(5.18m)),
            new Candle(
                DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                new Price(42000.1m), new Price(42250m), new Price(41950m), new Price(42200m), new Volume(12.5m), new Volume(525000.75m)),
        });
    }

    [Test]
    public async Task Non_zero_ret_code_is_an_error_with_bybit_message()
    {
        using StubHttpHandler handler = new StubHttpHandler().RespondJson(
            """{"retCode":10001,"retMsg":"params error: symbol invalid","result":{},"retExtInfo":{},"time":1704074400000}""");

        ExchangeException error = await Should.ThrowAsync<ExchangeException>(
            BybitClients.Create(handler, TimeProvider.System).GetCandlePagesAsync(BtcLinear, Interval.OneHour, From, To, CancellationToken.None).ToListAsync().AsTask());

        error.Message.ShouldBe("params error: symbol invalid");
    }
}
