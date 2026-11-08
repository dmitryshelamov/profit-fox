using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

using Google.Protobuf.WellKnownTypes;

using Npgsql;

using NSubstitute;

using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Exchanges.TInvest;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData;
using ProfitFox.Tests.Integration.Infrastructure;

using Shouldly;

using Tinkoff.InvestApi.V1;

using Instrument = ProfitFox.Domain.ValueObjects.MarketData.Instrument;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>Requirement "Граница покрытия серии" on an import of <c>moex</c> / <c>SBER</c> / <c>60</c> through the Worker, against the mock of T-Invest.</summary>
public class MoexCandleImportTests
{
    private const string Token = "t.token";
    private const string SberUid = "e6123145-9665-43e0-8413-cd61b8aa9b13";

    private static readonly DateTimeOffset Yesterday = DateTimeOffset.Parse("2024-03-09T00:00:00Z");
    private static readonly Instrument Sber = new Instrument(MarketCategory.Moex, "SBER");

    private ProfitFoxFactory _factory = null!;
    private WorkerHost _worker = null!;
    private HttpClient _client = null!;
    private List<GetCandlesRequest> _requests = null!;

    [SetUp]
    public async Task SetUp()
    {
        // The Worker decrypts the token of the account the API stored, so both run on the same master key.
        _factory = new ProfitFoxFactory(IntegrationEnvironment.ConnectionString) { Workers = 0 };
        _worker = _factory.StartWorker(workerSettings: new Dictionary<string, string?> { ["ExchangeAccounts:MasterKey"] = ProfitFoxFactory.TestMasterKey });
        _factory.BrokerAccounts.ListAccountsAsync(Token, Arg.Any<CancellationToken>())
            .Returns([new BrokerAccount("2000000001", "Брокерский счёт", BrokerAccountType.Brokerage, BrokerAccountStatus.Open, BrokerAccountAccess.Full)]);
        _requests = [];
        _factory.TInvest
            .On<InstrumentRequest, ShareResponse>("ShareBy", _ => new ShareResponse { Instrument = Sber10() })
            .On<GetCandlesRequest, GetCandlesResponse>(
                "GetCandles",
                request =>
                {
                    lock (_requests)
                    {
                        _requests.Add(request);
                    }

                    // A trading day: candles from 07:00 to 20:00 UTC, nothing at night; lots of 10 shares.
                    DateTimeOffset from = request.From.ToUtcMoment();
                    DateTimeOffset to = request.To.ToUtcMoment();
                    var response = new GetCandlesResponse();

                    for (DateTimeOffset open = Yesterday.AddHours(7); open <= Yesterday.AddHours(20); open = open.AddHours(1))
                    {
                        if (open >= from && open < to)
                        {
                            response.Candles.Add(new HistoricCandle
                            {
                                Time = Timestamp.FromDateTimeOffset(open),
                                Open = 305m.ToQuotation(),
                                High = 306m.ToQuotation(),
                                Low = 304m.ToQuotation(),
                                Close = 305.5m.ToQuotation(),
                                Volume = 12,
                                IsComplete = true
                            });
                        }
                    }

                    return response;
                });
        _client = _factory.CreateClient();
        HttpResponseMessage account = await _client.PostAsJsonAsync("/api/exchange-accounts", new { name = "Брокерский", exchange = "tinvest", token = Token, accountId = "2000000001" });
        account.EnsureSuccessStatusCode();
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _worker.DisposeAsync();
        await _factory.DisposeAsync();
    }

    // Scenarios "Лоты в акции" and "Ночь MOEX внутри покрытия": the second import of the same day asks T-Invest for nothing.
    [Test]
    public async Task Second_import_of_a_day_with_a_night_asks_tinvest_for_nothing()
    {
        JsonElement first = await ImportAsync(Yesterday, Yesterday.AddDays(1));

        first.GetProperty("status").GetString().ShouldBe("Succeeded", first.GetRawText());
        first.GetProperty("importedCandles").GetInt64().ShouldBe(14);
        List<JsonElement>? candles = await _client.GetFromJsonAsync<List<JsonElement>>("/api/market-data/candles?category=moex&symbol=SBER&interval=60&from=2024-03-09T00:00:00Z&to=2024-03-10T00:00:00Z");
        candles!.Count.ShouldBe(14);
        candles[0].GetProperty("volume").GetString().ShouldBe("120");
        int asked = _requests.Count;

        JsonElement second = await ImportAsync(Yesterday, Yesterday.AddDays(1));

        second.GetProperty("status").GetString().ShouldBe("Succeeded", second.GetRawText());
        second.GetProperty("importedCandles").GetInt64().ShouldBe(0);
        _requests.Count.ShouldBe(asked);
    }

    // Scenario "Период за границей": only the tail outside the coverage is asked for, and the border becomes the end of the period.
    [Test]
    public async Task Import_past_the_border_asks_only_for_the_tail_and_moves_the_border()
    {
        await using (var dataSource = NpgsqlDataSource.Create(_factory.ConnectionString))
        {
            await new SeriesCoverageRepository(dataSource).UpsertAsync(Sber, Interval.OneHour, new SeriesCoverage(Yesterday, Yesterday.AddHours(20)), CancellationToken.None);
        }

        JsonElement job = await ImportAsync(Yesterday, Yesterday.AddDays(1));

        job.GetProperty("status").GetString().ShouldBe("Succeeded", job.GetRawText());
        job.GetProperty("importedCandles").GetInt64().ShouldBe(1);
        _requests.Select(request => (request.From.ToUtcMoment(), request.To.ToUtcMoment())).ShouldBe([(Yesterday.AddHours(20), Yesterday.AddDays(1))]);
        await using var read = NpgsqlDataSource.Create(_factory.ConnectionString);
        (await new SeriesCoverageRepository(read).GetAsync(Sber, Interval.OneHour, CancellationToken.None)).ShouldBe(new SeriesCoverage(Yesterday, Yesterday.AddDays(1)));
    }

    private async Task<JsonElement> ImportAsync(DateTimeOffset from, DateTimeOffset to)
    {
        HttpResponseMessage created = await _client.PostAsJsonAsync(
            "/api/market-data/candle-imports",
            new { category = "moex", symbol = "SBER", interval = "60", from = from.ToString("O"), to = to.ToString("O") });
        created.EnsureSuccessStatusCode();
        Guid id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var waiting = Stopwatch.StartNew();

        while (true)
        {
            JsonElement job = await _client.GetFromJsonAsync<JsonElement>($"/api/market-data/candle-imports/{id}");

            if (job.GetProperty("status").GetString() is "Succeeded" or "Failed" || waiting.Elapsed > TimeSpan.FromSeconds(30))
            {
                return job;
            }

            // The Worker waits its poll interval on the app's clock.
            _factory.Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(50);
        }
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
}