using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Google.Protobuf.WellKnownTypes;

using NSubstitute;

using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Infrastructure.Exchanges.TInvest;
using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Fixtures.Backtests;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

using Tinkoff.InvestApi.V1;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>
///     Requirement "Бэктест на акциях MOEX": a backtest of the DCA template on <c>moex</c> / <c>SBER</c> / <c>60</c> over the two-day fixture
///     with the night between the days empty, the candles loaded from the mock of T-Invest, the lot 10 and the price step 0.01 of the share,
///     and the base rates of <c>moex</c> (0.05 % both ways).
/// </summary>
public class MoexBacktestTests
{
    private const string Token = "t.token";
    private const string SberUid = "e6123145-9665-43e0-8413-cd61b8aa9b13";

    private static readonly DateTimeOffset Day1 = DateTimeOffset.Parse("2024-03-04T00:00:00Z");
    private static readonly DateTimeOffset Day2 = DateTimeOffset.Parse("2024-03-05T00:00:00Z");

    private ProfitFoxFactory _factory = null!;
    private WorkerHost _worker = null!;
    private HttpClient _client = null!;
    private Guid _bot;

    [SetUp]
    public async Task SetUp()
    {
        _factory = new ProfitFoxFactory(IntegrationEnvironment.ConnectionString) { Workers = 0 };
        _worker = _factory.StartWorker(workerSettings: new Dictionary<string, string?> { ["ExchangeAccounts:MasterKey"] = ProfitFoxFactory.TestMasterKey });
        _factory.BrokerAccounts.ListAccountsAsync(Token, Arg.Any<CancellationToken>())
            .Returns([new BrokerAccount("2000000001", "Брокерский счёт", BrokerAccountType.Brokerage, BrokerAccountStatus.Open, BrokerAccountAccess.Full)]);
        _factory.TInvest
            .On<InstrumentRequest, ShareResponse>("ShareBy", _ => new ShareResponse { Instrument = Sber10() })
            .On<GetCandlesRequest, GetCandlesResponse>("GetCandles", request => Candles(request.From.ToUtcMoment(), request.To.ToUtcMoment()));
        _client = _factory.CreateClient();
        (await _client.PostAsJsonAsync("/api/exchange-accounts", new { name = "Брокерский", exchange = "tinvest", token = Token, accountId = "2000000001" })).EnsureSuccessStatusCode();
        HttpResponseMessage strategy = await _client.PostAsJsonAsync("/api/strategies", new { name = "dca", definition = StrategyFixtures.Element("dca") });
        Guid strategyId = (await strategy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        HttpResponseMessage bot = await _client.PostAsJsonAsync(
            "/api/bots",
            new JsonObject
            {
                ["name"] = "SBER DCA",
                ["strategyId"] = strategyId,
                ["parameterValues"] = new JsonObject { ["limitPerOrder"] = 10000, ["limitDeposit"] = 25000, ["purchaseInterval"] = "14:00:00" },
                ["category"] = "moex",
                ["symbol"] = "SBER",
                ["interval"] = "60",
                ["balance"] = 50000
            });
        bot.StatusCode.ShouldBe(HttpStatusCode.Created, await bot.Content.ReadAsStringAsync());
        _bot = (await bot.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _worker.DisposeAsync();
        await _factory.DisposeAsync();
    }

    // Scenarios "Покупка на сумму в лотах", "Ордер через ночь", "Ставки MOEX по умолчанию" and "Правила акции".
    [Test]
    public async Task Backtest_buys_whole_lots_fills_through_the_night_and_takes_the_fees_of_moex()
    {
        Guid id = await RunAsync();

        JsonElement backtest = await _client.GetFromJsonAsync<JsonElement>($"/api/backtests/{id}");

        // After the review: the nights inside the coverage are not gaps, and the whole series counts as loaded.
        backtest.GetProperty("gaps").GetArrayLength().ShouldBe(0, backtest.GetRawText());
        JsonElement progress = backtest.GetProperty("progress");
        (progress.GetProperty("loadedCandles").GetInt64(), progress.GetProperty("expectedCandles").GetInt64()).ShouldBe((28L, 28L));
        JsonElement rules = backtest.GetProperty("rules");
        (Number(rules, "tickSize"), Number(rules, "quantityStep"), Number(rules, "minQuantity"), Number(rules, "minAmount")).ShouldBe((0.01m, 10m, 10m, 0m));
        JsonElement simulation = backtest.GetProperty("simulation");
        (Number(simulation, "limitFeePercent"), Number(simulation, "marketFeePercent")).ShouldBe((0.05m, 0.05m));
        JsonElement details = await _client.GetFromJsonAsync<JsonElement>($"/api/backtests/{id}/results/0/details");
        JsonElement[] fills = [.. details.GetProperty("positions").EnumerateArray().SelectMany(position => position.GetProperty("orders").EnumerateArray()).SelectMany(order => order.GetProperty("fills").EnumerateArray())];
        fills.Length.ShouldBe(2);

        // 10 000 ₽ at the price of the fill buys whole lots of 10 shares: 30 shares around 305, never a fraction.
        decimal price = Number(fills[0], "price");
        decimal quantity = Number(fills[0], "quantity");
        quantity.ShouldBe(Math.Floor(10000m / price / 10m) * 10m);
        quantity.ShouldBe(30m);
        Number(fills[0], "fee").ShouldBe(Math.Round(quantity * price * 0.0005m, 8));
        fills[0].GetProperty("feeAsset").GetString().ShouldBe("quote");
        fills[0].GetProperty("at").GetDateTimeOffset().ShouldBe(Day1.AddHours(7));

        // The order placed on the close of the last candle of the first day fills on the first candle of the next day, not at night.
        fills[1].GetProperty("at").GetDateTimeOffset().ShouldBe(Day2.AddHours(7));
        JsonElement metrics = details.GetProperty("metrics");
        metrics.GetProperty("candles").GetInt32().ShouldBe(28);
        Number(metrics, "fees").ShouldBe(fills.Sum(fill => Number(fill, "fee")));
    }

    // Requirement "Детерминизм прогона" on the shares.
    [Test]
    public async Task Second_run_gives_the_same_result()
    {
        Guid first = await RunAsync();
        Guid second = await RunAsync();

        string firstMetrics = (await _client.GetFromJsonAsync<JsonElement>($"/api/backtests/{first}/results")).GetProperty("items")[0].GetProperty("metrics").GetRawText();
        string secondMetrics = (await _client.GetFromJsonAsync<JsonElement>($"/api/backtests/{second}/results")).GetProperty("items")[0].GetProperty("metrics").GetRawText();

        JsonNode.DeepEquals(JsonNode.Parse(firstMetrics), JsonNode.Parse(secondMetrics)).ShouldBeTrue();
    }

    private async Task<Guid> RunAsync()
    {
        HttpResponseMessage created = await _client.PostAsJsonAsync(
            $"/api/bots/{_bot}/backtests",
            new JsonObject { ["from"] = Day1.ToString("O"), ["to"] = Day2.AddDays(1).ToString("O"), ["parameters"] = new JsonObject() });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        Guid id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var waiting = Stopwatch.StartNew();
        JsonElement backtest = default;

        while (waiting.Elapsed < TimeSpan.FromSeconds(30))
        {
            backtest = await _client.GetFromJsonAsync<JsonElement>($"/api/backtests/{id}");

            if (backtest.GetProperty("status").GetString() is "Succeeded" or "Failed" or "Cancelled")
            {
                break;
            }

            _factory.Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(20);
        }

        backtest.GetProperty("status").GetString().ShouldBe("Succeeded", backtest.GetRawText());

        return id;
    }

    private static GetCandlesResponse Candles(DateTimeOffset from, DateTimeOffset to)
    {
        var response = new GetCandlesResponse();

        foreach (string[] row in CandleFixtures.SberHourly())
        {
            var open = DateTimeOffset.Parse(row[0], CultureInfo.InvariantCulture);

            if (open >= from && open < to)
            {
                response.Candles.Add(new HistoricCandle
                {
                    Time = Timestamp.FromDateTimeOffset(open),
                    Open = D(row[1]).ToQuotation(),
                    High = D(row[2]).ToQuotation(),
                    Low = D(row[3]).ToQuotation(),
                    Close = D(row[4]).ToQuotation(),
                    Volume = (long)(D(row[5]) / 10m),
                    IsComplete = true
                });
            }
        }

        return response;
    }

    private static Share Sber10()
    {
        return new Share
        {
            Uid = SberUid,
            Ticker = "SBER",
            ClassCode = "TQBR",
            Name = "Сбер Банк",
            Currency = "rub",
            Lot = 10,
            MinPriceIncrement = 0.01m.ToQuotation(),
            ApiTradeAvailableFlag = true
        };
    }

    private static decimal D(string value)
    {
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    private static decimal Number(JsonElement element, string name)
    {
        return D(element.GetProperty(name).GetString()!);
    }
}